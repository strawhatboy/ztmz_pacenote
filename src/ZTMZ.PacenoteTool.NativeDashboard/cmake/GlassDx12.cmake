if(TARGET ztmz_backend_dx12)
  set(dx12_glass_header "${CMAKE_CURRENT_BINARY_DIR}/generated/dx12/glass_shaders.hpp")
  add_custom_command(OUTPUT "${dx12_glass_header}"
    COMMAND ${CMAKE_COMMAND} -E make_directory "${CMAKE_CURRENT_BINARY_DIR}/generated/dx12"
    COMMAND $<TARGET_FILE:ztmz_compile_glass_shaders>
      "${CMAKE_CURRENT_SOURCE_DIR}/src/graphics/dx11/glass.hlsl" "${dx12_glass_header}" 5_0
    DEPENDS ztmz_compile_glass_shaders src/graphics/dx11/glass.hlsl VERBATIM)
  target_sources(ztmz_backend_dx12 PRIVATE src/graphics/dx12/glass.cpp "${dx12_glass_header}")
  target_include_directories(ztmz_backend_dx12 PRIVATE "${CMAKE_CURRENT_BINARY_DIR}/generated/dx12")
  if(BUILD_TESTING)
    add_executable(dx12_glass_tests tests/dx12_glass_tests.cpp)
    target_link_libraries(dx12_glass_tests PRIVATE ztmz_backend_dx12)
    add_test(NAME dx12_glass COMMAND dx12_glass_tests)
    set_tests_properties(dx12_glass PROPERTIES TIMEOUT 60)
  endif()
endif()
