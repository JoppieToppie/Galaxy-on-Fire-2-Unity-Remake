# The built-in screen recorder's encoder (docs/notes/platforms.md "Screen recorder"): gof2_recorder.dll, H.264 + AAC
# MP4 through Windows' Media Foundation (no SDK to fetch: the Windows SDK has it). Build (MSVC), the DLL goes to
# Assets/Plugins/Windows/x86_64 (the Editor loads it once: quit the Editor before installing a rebuilt one):
#   cmake -S Native/Recorder -B Native/Recorder/build -A x64
#   cmake --build Native/Recorder/build --config Release
#   cmake --install Native/Recorder/build --config Release
cmake_minimum_required(VERSION 3.20)
project(gof2_recorder CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_INSTALL_PREFIX "${CMAKE_CURRENT_SOURCE_DIR}/../../Assets/Plugins/Windows/x86_64" CACHE PATH "" FORCE)

add_library(gof2_recorder SHARED gof2_recorder.cpp)
target_compile_definitions(gof2_recorder PRIVATE UNICODE _UNICODE _CRT_SECURE_NO_WARNINGS)
set_target_properties(gof2_recorder PROPERTIES MSVC_RUNTIME_LIBRARY "MultiThreaded")   # no VC runtime DLLs to ship
target_compile_options(gof2_recorder PRIVATE /W3 /EHsc)
target_link_libraries(gof2_recorder PRIVATE mfplat mfreadwrite mfuuid ole32)

install(TARGETS gof2_recorder RUNTIME DESTINATION .)
