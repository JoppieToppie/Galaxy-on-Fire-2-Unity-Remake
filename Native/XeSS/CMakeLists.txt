# Intel XeSS (docs/notes/options.md "XeSS"): the Unity native plugin gof2_xess.dll, which runs XeSS Super Resolution for
# XessUpscaler in the Windows D3D12 player. Needs Intel's XeSS SDK in sdk/ (git-ignored; its headers, import library and
# libxess.dll):
#   git clone --depth 1 https://github.com/intel/xess Native/XeSS/sdk
# Build (MSVC), the DLL goes to Assets/Plugins/Windows/x86_64 (libxess.dll is copied into a build by XessRedist):
#   cmake -S Native/XeSS -B Native/XeSS/build -A x64
#   cmake --build Native/XeSS/build --config Release
#   cmake --install Native/XeSS/build --config Release
cmake_minimum_required(VERSION 3.20)
project(gof2_xess CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(XESS_SDK "${CMAKE_CURRENT_SOURCE_DIR}/sdk" CACHE PATH "Intel's XeSS SDK checkout (inc/, lib/, bin/)")
set(UNITY_PLUGIN_API "C:/Program Files/Unity/Hub/Editor/7000.0.0a20/Editor/Data/PluginAPI" CACHE PATH "Unity's PluginAPI headers")
set(CMAKE_INSTALL_PREFIX "${CMAKE_CURRENT_SOURCE_DIR}/../../Assets/Plugins/Windows/x86_64" CACHE PATH "" FORCE)

add_library(gof2_xess SHARED gof2_xess.cpp)
target_include_directories(gof2_xess PRIVATE "${UNITY_PLUGIN_API}" "${XESS_SDK}/inc")
target_compile_definitions(gof2_xess PRIVATE UNICODE _UNICODE _CRT_SECURE_NO_WARNINGS)
set_target_properties(gof2_xess PROPERTIES MSVC_RUNTIME_LIBRARY "MultiThreaded")   # no VC runtime DLLs to ship
target_compile_options(gof2_xess PRIVATE /W3 /EHsc)
# libxess.dll is delay-loaded: the plugin loads without it (and says XeSS is not there).
target_link_libraries(gof2_xess PRIVATE "${XESS_SDK}/lib/libxess.lib" delayimp)
target_link_options(gof2_xess PRIVATE /DELAYLOAD:libxess.dll)

install(TARGETS gof2_xess RUNTIME DESTINATION .)
