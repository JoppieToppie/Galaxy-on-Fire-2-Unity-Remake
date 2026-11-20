# Frame generation (docs/notes/options.md "Frame generation"): the Unity native plugin gof2_streamline.dll, which puts
# NVIDIA Streamline (DLSS Frame Generation) between the Windows D3D12 player and its swap chain. Needs the Streamline SDK's
# headers of the DLLs' version in sdk/ (git-ignored):
#   git clone --depth 1 --branch v2.14.16 --filter=blob:none --sparse https://github.com/NVIDIA-RTX/Streamline Native/Streamline/sdk
#   git -C Native/Streamline/sdk sparse-checkout set include docs
# Build (MSVC), the DLL goes to Assets/Plugins/Windows/x86_64:
#   cmake -S Native/Streamline -B Native/Streamline/build -A x64
#   cmake --build Native/Streamline/build --config Release
#   cmake --install Native/Streamline/build --config Release
cmake_minimum_required(VERSION 3.20)
project(gof2_streamline CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(STREAMLINE_SDK "${CMAKE_CURRENT_SOURCE_DIR}/sdk" CACHE PATH "The Streamline SDK checkout (include/)")
set(UNITY_PLUGIN_API "C:/Program Files/Unity/Hub/Editor/7000.0.0a16/Editor/Data/PluginAPI" CACHE PATH "Unity's PluginAPI headers")
set(CMAKE_INSTALL_PREFIX "${CMAKE_CURRENT_SOURCE_DIR}/../../Assets/Plugins/Windows/x86_64" CACHE PATH "" FORCE)

# sl.interposer.dll's import library from its export names: it is delay-loaded, so the plugin loads without Streamline's
# DLLs (and never touches them unless the option armed it).
set(SL_IMPORT_LIB "${CMAKE_CURRENT_BINARY_DIR}/sl.interposer.lib")
add_custom_command(OUTPUT "${SL_IMPORT_LIB}"
    COMMAND lib /nologo /def:"${CMAKE_CURRENT_SOURCE_DIR}/sl_interposer.def" /out:"${SL_IMPORT_LIB}" /machine:x64
    DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/sl_interposer.def")
add_custom_target(sl_import_lib DEPENDS "${SL_IMPORT_LIB}")

add_library(gof2_streamline SHARED gof2_streamline.cpp)
add_dependencies(gof2_streamline sl_import_lib)
set(ANTILAG2_SDK "${CMAKE_CURRENT_SOURCE_DIR}/../AntiLag2/sdk" CACHE PATH "AMD's Anti-Lag 2 SDK (ffx_antilag2_dx12.h)")
# AMD's FidelityFX SDK headers for FSR frame generation (the version of the loader Unity ships beside the player):
#   git clone --depth 1 --branch v2.3.0 --filter=blob:none --sparse https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK Native/FsrFrameGen/sdk
#   git -C Native/FsrFrameGen/sdk sparse-checkout set Kits/FidelityFX/api/include Kits/FidelityFX/framegeneration/include Kits/FidelityFX/signedbin
set(FFX_SDK "${CMAKE_CURRENT_SOURCE_DIR}/../FsrFrameGen/sdk/Kits/FidelityFX" CACHE PATH "AMD's FidelityFX SDK kit (api/, framegeneration/)")
target_include_directories(gof2_streamline PRIVATE "${UNITY_PLUGIN_API}" "${STREAMLINE_SDK}/include" "${ANTILAG2_SDK}" "${FFX_SDK}")
target_compile_definitions(gof2_streamline PRIVATE UNICODE _UNICODE _CRT_SECURE_NO_WARNINGS)
set_target_properties(gof2_streamline PROPERTIES MSVC_RUNTIME_LIBRARY "MultiThreaded")   # no VC runtime DLLs to ship
target_compile_options(gof2_streamline PRIVATE /W3 /EHsc)
target_link_libraries(gof2_streamline PRIVATE "${SL_IMPORT_LIB}" delayimp dxguid wintrust crypt32 advapi32)
target_link_options(gof2_streamline PRIVATE /DELAYLOAD:sl.interposer.dll)

install(TARGETS gof2_streamline RUNTIME DESTINATION .)
