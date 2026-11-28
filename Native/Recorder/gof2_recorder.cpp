// gof2_recorder.cpp
// The built-in screen recorder's encoder (docs/notes/platforms.md "Screen recorder"; UI.ScreenRecorder drives it): an MP4
// with H.264 video and AAC sound through Windows' own Media Foundation sink writer (a hardware encoder when the GPU has
// one: MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS), so no codec ships with the game; with an HDR screen HEVC Main 10 (HDR10:
// BT.2020, PQ, the screen's light levels as metadata) when the PC has an HEVC encoder (gof2rec_start fails otherwise and
// the game records SDR). The game hands it frames (planar YUV 4:2:0 its shader packed on the GPU, the top row first:
// 8-bit I420, or 16-bit samples with the 10 bits high, which go in as P010 with U and V interleaved here) and sound
// (float, interleaved) with their times; they are copied into a queue and
// one worker thread owns every Media Foundation object: it creates the writer, encodes the queue and finalizes the file.
// A full queue (the encoder behind) drops video frames, never sound.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <mferror.h>

#include <atomic>
#include <condition_variable>
#include <cstdint>
#include <cstring>
#include <deque>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

namespace
{
    struct Item
    {
        bool video;
        std::vector<uint8_t> data;
        LONGLONG time, duration;   // 100 ns units
    };

    const size_t MaxQueuedBytes = 512u * 1024u * 1024u;

    std::mutex lock;
    std::condition_variable wake, ready;
    std::deque<Item> queue;
    size_t queuedBytes = 0;
    std::thread worker;
    bool running = false, stopping = false, started = false;
    HRESULT startResult = S_OK, writeResult = S_OK;
    std::atomic<int> dropped{0};
    int width = 0, height = 0, fps = 60, kbps = 16000, sampleRate = 0, channels = 0, maxNits = 1000, averageNits = 200;
    bool audio = false, hdr = false;
    std::wstring path;

    template <class T> void Release(T*& p) { if (p) { p->Release(); p = nullptr; } }

    // HD video's colours (the recorder's shader packs BT.709, limited range), so players don't guess BT.601.
    HRESULT Bt709(IMFMediaType* t)
    {
        HRESULT hr = t->SetUINT32(MF_MT_YUV_MATRIX, MFVideoTransferMatrix_BT709);
        if (SUCCEEDED(hr)) hr = t->SetUINT32(MF_MT_VIDEO_PRIMARIES, MFVideoPrimaries_BT709);
        if (SUCCEEDED(hr)) hr = t->SetUINT32(MF_MT_TRANSFER_FUNCTION, MFVideoTransFunc_709);
        if (SUCCEEDED(hr)) hr = t->SetUINT32(MF_MT_VIDEO_NOMINAL_RANGE, MFNominalRange_16_235);
        return hr;
    }

    // HDR10: BT.2020 primaries and matrix, the PQ curve, limited range, the light levels the screen shows.
    HRESULT Hdr10(IMFMediaType* t)
    {
        HRESULT hr = t->SetUINT32(MF_MT_YUV_MATRIX, MFVideoTransferMatrix_BT2020_10);
        if (SUCCEEDED(hr)) hr = t->SetUINT32(MF_MT_VIDEO_PRIMARIES, MFVideoPrimaries_BT2020);
        if (SUCCEEDED(hr)) hr = t->SetUINT32(MF_MT_TRANSFER_FUNCTION, MFVideoTransFunc_2084);
        if (SUCCEEDED(hr)) hr = t->SetUINT32(MF_MT_VIDEO_NOMINAL_RANGE, MFNominalRange_16_235);
        if (SUCCEEDED(hr)) hr = t->SetUINT32(MF_MT_MAX_LUMINANCE_LEVEL, (UINT32)maxNits);
        if (SUCCEEDED(hr)) hr = t->SetUINT32(MF_MT_MAX_FRAME_AVERAGE_LUMINANCE_LEVEL, (UINT32)averageNits);
        if (SUCCEEDED(hr)) hr = t->SetUINT32(MF_MT_MAX_MASTERING_LUMINANCE, (UINT32)maxNits);
        if (SUCCEEDED(hr)) hr = t->SetUINT32(MF_MT_MIN_MASTERING_LUMINANCE, 50);   // 0.005 nits, in 1/10000
        return hr;
    }

    HRESULT AddVideo(IMFSinkWriter* w, DWORD* stream)
    {
        IMFMediaType* out = nullptr;
        IMFMediaType* in = nullptr;
        HRESULT hr = MFCreateMediaType(&out);
        if (SUCCEEDED(hr)) hr = out->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
        if (SUCCEEDED(hr)) hr = out->SetGUID(MF_MT_SUBTYPE, hdr ? MFVideoFormat_HEVC : MFVideoFormat_H264);
        if (SUCCEEDED(hr) && hdr) hr = out->SetUINT32(MF_MT_MPEG2_PROFILE, 2);   // eAVEncH265VProfile_Main_420_10
        if (SUCCEEDED(hr)) hr = out->SetUINT32(MF_MT_AVG_BITRATE, (UINT32)kbps * 1000u);
        if (SUCCEEDED(hr)) hr = out->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
        if (SUCCEEDED(hr)) hr = MFSetAttributeSize(out, MF_MT_FRAME_SIZE, width, height);
        if (SUCCEEDED(hr)) hr = MFSetAttributeRatio(out, MF_MT_FRAME_RATE, fps, 1);
        if (SUCCEEDED(hr)) hr = MFSetAttributeRatio(out, MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
        if (SUCCEEDED(hr)) hr = hdr ? Hdr10(out) : Bt709(out);
        if (SUCCEEDED(hr)) hr = w->AddStream(out, stream);
        if (SUCCEEDED(hr)) hr = MFCreateMediaType(&in);
        if (SUCCEEDED(hr)) hr = in->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
        if (SUCCEEDED(hr)) hr = in->SetGUID(MF_MT_SUBTYPE, hdr ? MFVideoFormat_P010 : MFVideoFormat_I420);
        if (SUCCEEDED(hr)) hr = in->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
        if (SUCCEEDED(hr)) hr = in->SetUINT32(MF_MT_DEFAULT_STRIDE, (UINT32)(hdr ? width * 2 : width));
        if (SUCCEEDED(hr)) hr = MFSetAttributeSize(in, MF_MT_FRAME_SIZE, width, height);
        if (SUCCEEDED(hr)) hr = MFSetAttributeRatio(in, MF_MT_FRAME_RATE, fps, 1);
        if (SUCCEEDED(hr)) hr = MFSetAttributeRatio(in, MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
        if (SUCCEEDED(hr)) hr = hdr ? Hdr10(in) : Bt709(in);
        if (SUCCEEDED(hr)) hr = w->SetInputMediaType(*stream, in, nullptr);
        Release(out);
        Release(in);
        return hr;
    }

    // AAC at 192 kbit/s, stereo 16-bit PCM in (the AAC encoder takes 44.1 or 48 kHz).
    HRESULT AddAudio(IMFSinkWriter* w, DWORD* stream)
    {
        IMFMediaType* out = nullptr;
        IMFMediaType* in = nullptr;
        HRESULT hr = MFCreateMediaType(&out);
        if (SUCCEEDED(hr)) hr = out->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio);
        if (SUCCEEDED(hr)) hr = out->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_AAC);
        if (SUCCEEDED(hr)) hr = out->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
        if (SUCCEEDED(hr)) hr = out->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, sampleRate);
        if (SUCCEEDED(hr)) hr = out->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, 2);
        if (SUCCEEDED(hr)) hr = out->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, 24000);
        if (SUCCEEDED(hr)) hr = w->AddStream(out, stream);
        if (SUCCEEDED(hr)) hr = MFCreateMediaType(&in);
        if (SUCCEEDED(hr)) hr = in->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio);
        if (SUCCEEDED(hr)) hr = in->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_PCM);
        if (SUCCEEDED(hr)) hr = in->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
        if (SUCCEEDED(hr)) hr = in->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, sampleRate);
        if (SUCCEEDED(hr)) hr = in->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, 2);
        if (SUCCEEDED(hr)) hr = in->SetUINT32(MF_MT_AUDIO_BLOCK_ALIGNMENT, 4);
        if (SUCCEEDED(hr)) hr = in->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, sampleRate * 4);
        if (SUCCEEDED(hr)) hr = w->SetInputMediaType(*stream, in, nullptr);
        Release(out);
        Release(in);
        return hr;
    }

    HRESULT Write(IMFSinkWriter* w, DWORD stream, const Item& item)
    {
        IMFMediaBuffer* buffer = nullptr;
        IMFSample* sample = nullptr;
        BYTE* p = nullptr;
        HRESULT hr = MFCreateMemoryBuffer((DWORD)item.data.size(), &buffer);
        if (SUCCEEDED(hr)) hr = buffer->Lock(&p, nullptr, nullptr);
        if (SUCCEEDED(hr)) { memcpy(p, item.data.data(), item.data.size()); buffer->Unlock(); }
        if (SUCCEEDED(hr)) hr = buffer->SetCurrentLength((DWORD)item.data.size());
        if (SUCCEEDED(hr)) hr = MFCreateSample(&sample);
        if (SUCCEEDED(hr)) hr = sample->AddBuffer(buffer);
        if (SUCCEEDED(hr)) hr = sample->SetSampleTime(item.time);
        if (SUCCEEDED(hr)) hr = sample->SetSampleDuration(item.duration);
        if (SUCCEEDED(hr)) hr = w->WriteSample(stream, sample);
        Release(sample);
        Release(buffer);
        return hr;
    }

    void Run()
    {
        HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        bool com = SUCCEEDED(hr);
        hr = MFStartup(MF_VERSION, MFSTARTUP_LITE);
        bool mf = SUCCEEDED(hr);
        IMFAttributes* attributes = nullptr;
        IMFSinkWriter* writer = nullptr;
        DWORD videoStream = 0, audioStream = 0;
        if (SUCCEEDED(hr)) hr = MFCreateAttributes(&attributes, 2);
        if (SUCCEEDED(hr)) hr = attributes->SetUINT32(MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, TRUE);
        if (SUCCEEDED(hr)) hr = attributes->SetGUID(MF_TRANSCODE_CONTAINERTYPE, MFTranscodeContainerType_MPEG4);
        if (SUCCEEDED(hr)) hr = MFCreateSinkWriterFromURL(path.c_str(), nullptr, attributes, &writer);
        if (SUCCEEDED(hr)) hr = AddVideo(writer, &videoStream);
        if (SUCCEEDED(hr) && audio && FAILED(AddAudio(writer, &audioStream))) audio = false;   // the picture without sound
        if (SUCCEEDED(hr)) hr = writer->BeginWriting();
        {
            std::lock_guard<std::mutex> g(lock);
            startResult = hr;
            started = true;
        }
        ready.notify_all();
        if (SUCCEEDED(hr))
        {
            for (;;)
            {
                Item item;
                {
                    std::unique_lock<std::mutex> g(lock);
                    wake.wait(g, [] { return stopping || !queue.empty(); });
                    if (queue.empty()) break;   // stopping, and everything written
                    item = std::move(queue.front());
                    queue.pop_front();
                    queuedBytes -= item.data.size();
                }
                if (!item.video && !audio) continue;
                HRESULT w = Write(writer, item.video ? videoStream : audioStream, item);
                if (FAILED(w) && SUCCEEDED(writeResult)) writeResult = w;
            }
            HRESULT f = writer->Finalize();
            if (FAILED(f) && SUCCEEDED(writeResult)) writeResult = f;
        }
        Release(writer);
        Release(attributes);
        if (mf) MFShutdown();
        if (com) CoUninitialize();
    }

    void Push(Item&& item)
    {
        {
            std::lock_guard<std::mutex> g(lock);
            if (!running || stopping) return;
            if (item.video && queuedBytes + item.data.size() > MaxQueuedBytes) { dropped++; return; }
            queuedBytes += item.data.size();
            queue.push_back(std::move(item));
        }
        wake.notify_one();
    }
}

extern "C"
{
    // Starts a file: 'w' and 'h' multiples of 4, 'rate' 44100 or 48000 for sound (0: none); 'hdr10' HEVC Main 10 with
    // the screen's peak and average light (nits). Returns 0 or the failing HRESULT (no HEVC encoder: MF_E_TOPO_CODEC_NOT_FOUND
    // and the like, the game then records SDR).
    __declspec(dllexport) int gof2rec_start(const wchar_t* file, int w, int h, int frameRate, int bitrateKbps, int rate, int soundChannels,
                                            int hdr10, int peakNits, int averageLight)
    {
        std::unique_lock<std::mutex> g(lock);
        if (running) return (int)E_ILLEGAL_METHOD_CALL;
        if (w <= 0 || h <= 0 || (w & 3) || (h & 3) || frameRate <= 0) return (int)E_INVALIDARG;
        path = file;
        width = w; height = h; fps = frameRate; kbps = bitrateKbps;
        sampleRate = rate; channels = soundChannels;
        hdr = hdr10 != 0;
        maxNits = peakNits > 0 ? peakNits : 1000;
        averageNits = averageLight > 0 ? averageLight : 200;
        audio = (rate == 44100 || rate == 48000) && soundChannels > 0;
        queue.clear();
        queuedBytes = 0;
        dropped = 0;
        stopping = false;
        started = false;
        startResult = S_OK;
        writeResult = S_OK;
        running = true;
        worker = std::thread(Run);
        ready.wait(g, [] { return started; });
        if (FAILED(startResult))
        {
            running = false;
            g.unlock();
            worker.join();
            return (int)startResult;
        }
        return 0;
    }

    // A frame at 'time' (100 ns from the start): 'w' x 'h' planar 4:2:0, the top row first: I420 (w * h * 3 / 2 bytes), or
    // in HDR 16-bit samples (twice that), the U and V planes interleaved here into P010's.
    __declspec(dllexport) void gof2rec_video(const uint8_t* yuv, int w, int h, long long time)
    {
        if (yuv == nullptr || w != width || h != height) return;
        size_t samples = (size_t)w * h * 3 / 2;
        if (!hdr)
        {
            Push(Item{true, std::vector<uint8_t>(yuv, yuv + samples), time, 10000000LL / fps});
            return;
        }
        Item item{true, std::vector<uint8_t>(samples * 2), time, 10000000LL / fps};
        const auto* in = reinterpret_cast<const uint16_t*>(yuv);
        auto* out = reinterpret_cast<uint16_t*>(item.data.data());
        size_t luma = (size_t)w * h, quarter = luma / 4;
        memcpy(out, in, luma * 2);
        for (size_t i = 0; i < quarter; i++)
        {
            out[luma + i * 2] = in[luma + i];
            out[luma + i * 2 + 1] = in[luma + quarter + i];
        }
        Push(std::move(item));
    }

    // Sound: 'frames' frames of 'count' interleaved float channels (the first two kept, mono doubled) at 'time'.
    __declspec(dllexport) void gof2rec_audio(const float* samples, int frames, int count, long long time)
    {
        if (samples == nullptr || frames <= 0 || count <= 0 || sampleRate <= 0) return;
        Item item{false, std::vector<uint8_t>((size_t)frames * 4), time, (LONGLONG)frames * 10000000LL / sampleRate};
        auto* out = reinterpret_cast<int16_t*>(item.data.data());
        for (int i = 0; i < frames; i++)
        {
            float l = samples[(size_t)i * count], r = count > 1 ? samples[(size_t)i * count + 1] : l;
            l = l < -1.f ? -1.f : l > 1.f ? 1.f : l;
            r = r < -1.f ? -1.f : r > 1.f ? 1.f : r;
            out[i * 2] = (int16_t)(l * 32767.f);
            out[i * 2 + 1] = (int16_t)(r * 32767.f);
        }
        Push(std::move(item));
    }

    // Writes what is queued and closes the file. Returns 0 or the first failing HRESULT.
    __declspec(dllexport) int gof2rec_stop()
    {
        {
            std::lock_guard<std::mutex> g(lock);
            if (!running) return 0;
            stopping = true;
        }
        wake.notify_all();
        worker.join();
        std::lock_guard<std::mutex> g(lock);
        running = false;
        return (int)writeResult;
    }

    __declspec(dllexport) int gof2rec_dropped() { return dropped.load(); }
    __declspec(dllexport) int gof2rec_has_audio() { return audio ? 1 : 0; }
}
