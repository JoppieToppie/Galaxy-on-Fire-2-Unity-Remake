# FSR on Linux (docs/notes/options.md "FSR on Linux"): the Unity native plugin libgof2_fsr.so and the ffx-vk-linux library it
# uses (https://github.com/DuIslingr/ffx-vk-linux, built from its checkout: FFXVK_DIR). Both go to Assets/Plugins/Linux/x86_64:
#   cmake -S Native/FsrLinux -B Native/FsrLinux/build -DCMAKE_BUILD_TYPE=Release -DFFXVK_DIR=$HOME/Github/ffx-vk-linux
#   cmake --build Native/FsrLinux/build -j && cmake --install Native/FsrLinux/build
cmake_minimum_required(VERSION 3.20)
project(gof2_fsr CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(FFXVK_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../../ffx-vk-linux" CACHE PATH "The ffx-vk-linux checkout")
set(UNITY_PLUGIN_API "$ENV{HOME}/Unity/Hub/Editor/7000.0.0a20/Editor/Data/PluginAPI" CACHE PATH "Unity's PluginAPI headers")
set(CMAKE_INSTALL_PREFIX "${CMAKE_CURRENT_SOURCE_DIR}/../../Assets/Plugins/Linux/x86_64" CACHE PATH "" FORCE)

# OpenSSL 3 where the system's is newer (Arch's openssl-3 package): ffx-vk-linux builds with either, but players' systems
# mostly have libcrypto.so.3
if(EXISTS /usr/include/openssl-3/openssl/x509.h AND NOT OPENSSL_INCLUDE_DIR)
    set(OPENSSL_INCLUDE_DIR /usr/include/openssl-3 CACHE PATH "")
    set(OPENSSL_CRYPTO_LIBRARY /usr/lib/openssl-3/libcrypto.so CACHE FILEPATH "")
    set(OPENSSL_SSL_LIBRARY /usr/lib/openssl-3/libssl.so CACHE FILEPATH "")
endif()

# ffx-vk-linux's own targets (its tests and tools stay out of the default build)
set(BUILD_TESTING OFF CACHE BOOL "" FORCE)
add_subdirectory("${FFXVK_DIR}" ffx-vk-linux EXCLUDE_FROM_ALL)

add_library(gof2_fsr SHARED gof2_fsr.cpp)
target_include_directories(gof2_fsr PRIVATE "${UNITY_PLUGIN_API}")
target_link_libraries(gof2_fsr PRIVATE ffx_vk_linux)
set_target_properties(gof2_fsr PROPERTIES CXX_VISIBILITY_PRESET hidden BUILD_RPATH "\$ORIGIN" INSTALL_RPATH "\$ORIGIN")
set_target_properties(ffx_vk_linux PROPERTIES INSTALL_RPATH "\$ORIGIN")

install(TARGETS gof2_fsr ffx_vk_linux LIBRARY DESTINATION .)
