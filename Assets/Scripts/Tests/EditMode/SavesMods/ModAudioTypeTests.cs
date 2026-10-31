// ModAudioTypeTests.cs
// Feature: CLAUDE.md "Mods" > Sound effects (2026-10-24, a player's report: a WAV added in the Mod Builder's Audio kind
// "couldn't be played"): every platform's file picker copies the picked file as "picked.tmp", and ModProject.SetAudio took
// the extension from that copy, so each WAV / MP3 was saved as .ogg and decoded as Ogg Vorbis. ModAudio.Sniff tells the
// type from the file's first bytes; ModAudio.Load goes by it, so the files already saved that way play.

using GoF2Remake.Modding;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ModAudioTypeTests
    {
        static byte[] Bytes(string s) => System.Text.Encoding.ASCII.GetBytes(s);

        [Test]
        public void AnAudioFileIsKnownByItsFirstBytes()
        {
            var wav = Bytes("RIFF\x46\xc8\x01\x00WAVEfmt ");
            Assert.AreEqual(AudioType.WAV, ModAudio.Sniff(wav, wav.Length));
            var ogg = Bytes("OggS\0\x02\0\0\0\0\0\0");
            Assert.AreEqual(AudioType.OGGVORBIS, ModAudio.Sniff(ogg, ogg.Length));
            var id3 = Bytes("ID3\x04\0\0\0\0\0\0\0\0");
            Assert.AreEqual(AudioType.MPEG, ModAudio.Sniff(id3, id3.Length));
            var frame = new byte[] { 0xFF, 0xFB, 0x90, 0x64, 0, 0, 0, 0, 0, 0, 0, 0 };
            Assert.AreEqual(AudioType.MPEG, ModAudio.Sniff(frame, frame.Length), "an MP3 without a tag starts on a frame sync");

            var riffOther = Bytes("RIFF\0\0\0\0AVI LIST");
            Assert.AreEqual(AudioType.UNKNOWN, ModAudio.Sniff(riffOther, riffOther.Length), "a RIFF that isn't WAVE");
            var png = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 0, 0, 0, 13 };
            Assert.AreEqual(AudioType.UNKNOWN, ModAudio.Sniff(png, png.Length));
            Assert.AreEqual(AudioType.UNKNOWN, ModAudio.Sniff(wav, 2), "too short to tell");

            Assert.AreEqual(".wav", ModAudio.ExtensionOf(AudioType.WAV));
            Assert.AreEqual(".ogg", ModAudio.ExtensionOf(AudioType.OGGVORBIS));
            Assert.AreEqual(".mp3", ModAudio.ExtensionOf(AudioType.MPEG));
            Assert.IsNull(ModAudio.ExtensionOf(AudioType.UNKNOWN));
        }

        [Test]
        public void AWavSavedUnderAnOggNameIsAWav()
        {
            string path = System.IO.Path.Combine(Application.temporaryCachePath, "t_sniff_" + System.Guid.NewGuid().ToString("N") + ".ogg");
            try
            {
                System.IO.File.WriteAllBytes(path, Bytes("RIFF\x24\0\0\0WAVEfmt \x10\0\0\0"));
                Assert.AreEqual(AudioType.OGGVORBIS, ModAudio.TypeOf(path), "the name says Ogg");
                Assert.AreEqual(AudioType.WAV, ModAudio.SniffFile(path), "the content says WAV");
                Assert.AreEqual(AudioType.UNKNOWN, ModAudio.SniffFile(path + ".missing"));
            }
            finally { System.IO.File.Delete(path); }
        }

        [Test]
        public void TheContentDecidesWhateverTheName()
        {
            // #208: before the picker named its copy by type, the weapon fx page saved a picked sound as "fx/<item>.tmp"
            // (Android's picker, like every platform's, copies to "picked.tmp"); the name alone refused it.
            Assert.AreEqual(AudioType.OGGVORBIS, ModAudio.TypeOf("fx/plasma_lance.tmp", AudioType.OGGVORBIS));
            Assert.AreEqual(AudioType.WAV, ModAudio.TypeOf("sounds/shot.ogg", AudioType.WAV), "a WAV saved as .ogg");
            Assert.AreEqual(AudioType.MPEG, ModAudio.TypeOf("fx/shot.mp3", AudioType.UNKNOWN), "unreadable head: the name");
            Assert.AreEqual(AudioType.UNKNOWN, ModAudio.TypeOf("fx/shot.tmp", AudioType.UNKNOWN), "neither shows a type");
        }
    }
}
