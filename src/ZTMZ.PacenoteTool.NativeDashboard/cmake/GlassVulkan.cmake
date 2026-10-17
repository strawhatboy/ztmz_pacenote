if(NOT TARGET ztmz_backend_vulkan)
  return()
endif()
# Vulkan SDK installs DXC with SPIR-V support. Also look in installed Windows SDKs.
file(GLOB _ztmz_sdk_dxc "C:/Program Files (x86)/Windows Kits/10/bin/*/x64/dxc.exe")
list(SORT _ztmz_sdk_dxc COMPARE NATURAL ORDER DESCENDING)
set(_ztmz_dxc_hints "$ENV{VULKAN_SDK}/Bin")
foreach(_candidate IN LISTS _ztmz_sdk_dxc)
  get_filename_component(_directory "${_candidate}" DIRECTORY)
  list(APPEND _ztmz_dxc_hints "${_directory}")
endforeach()
find_program(ZTMZ_VULKAN_DXC NAMES dxc HINTS ${_ztmz_dxc_hints}
  DOC "DXC executable with SPIR-V support, used only at build time")
set(_glass_shader "${CMAKE_CURRENT_SOURCE_DIR}/src/graphics/vulkan/glass.hlsl")
set(_glass_header "${CMAKE_CURRENT_SOURCE_DIR}/src/graphics/vulkan/vulkan_glass_shaders.hpp")
set(_glass_vs "${CMAKE_CURRENT_BINARY_DIR}/generated/vulkan_glass_vs.spv")
set(_glass_ps "${CMAKE_CURRENT_BINARY_DIR}/generated/vulkan_glass_ps.spv")
if(ZTMZ_VULKAN_DXC)
# Explicit developer target only. Some SDK DXC binaries advertise -spirv in help
# but reject it at compile time, so normal builds never invoke the compiler.
add_custom_target(regenerate_vulkan_glass_shaders
  COMMAND "${CMAKE_COMMAND}" -E make_directory "${CMAKE_CURRENT_BINARY_DIR}/generated"
  COMMAND "${ZTMZ_VULKAN_DXC}" -spirv -fspv-target-env=vulkan1.0 -T vs_6_0 -E vsMain -O3 -Fo "${_glass_vs}" "${_glass_shader}"
  COMMAND "${ZTMZ_VULKAN_DXC}" -spirv -fspv-target-env=vulkan1.0 -T ps_6_0 -E psMain -O3 -Fo "${_glass_ps}" "${_glass_shader}"
  COMMAND "${CMAKE_COMMAND}" "-DOUTPUT=${_glass_header}" "-DSOURCE=${_glass_shader}" "-Dvs=${_glass_vs}" "-Dps=${_glass_ps}"
    -P "${CMAKE_CURRENT_SOURCE_DIR}/src/graphics/vulkan/embed_spirv.cmake"
  DEPENDS "${_glass_shader}" src/graphics/vulkan/embed_spirv.cmake
  BYPRODUCTS "${_glass_vs}" "${_glass_ps}" VERBATIM)
endif()
file(READ "${_glass_shader}" _glass_source)
string(REPLACE "\r\n" "\n" _glass_source "${_glass_source}")
string(SHA256 _glass_source_hash "${_glass_source}")
file(STRINGS "${_glass_header}" _glass_recorded_hash REGEX "^// Source SHA256: ")
if(NOT _glass_recorded_hash STREQUAL "// Source SHA256: ${_glass_source_hash}")
  message(FATAL_ERROR "Vulkan glass HLSL differs from checked-in SPIR-V. Regenerate vulkan_glass_shaders.hpp with SPIR-V-enabled DXC (see src/graphics/vulkan/README.md).")
endif()
target_sources(ztmz_backend_vulkan PRIVATE src/graphics/vulkan/glass.cpp "${_glass_header}")
if(BUILD_TESTING)
  add_executable(vulkan_glass_tests tests/vulkan_glass_tests.cpp)
  target_link_libraries(vulkan_glass_tests PRIVATE ztmz_backend_vulkan)
  add_test(NAME vulkan_glass COMMAND vulkan_glass_tests)
  set_tests_properties(vulkan_glass PROPERTIES TIMEOUT 90 RUN_SERIAL TRUE SKIP_RETURN_CODE 77)
endif()
