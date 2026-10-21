find_package(Threads REQUIRED)

add_library(archoera_core SHARED
  src/archoera_core.cpp
  src/method_registry.cpp
  src/module_host.cpp
  src/module_registry.cpp
)

target_compile_features(archoera_core PRIVATE cxx_std_20)
target_include_directories(archoera_core
  PUBLIC "${CMAKE_CURRENT_LIST_DIR}/../contracts/abi"
  PRIVATE "${CMAKE_CURRENT_LIST_DIR}/../contracts/module"
)
target_compile_definitions(archoera_core PRIVATE ARCHOERA_CORE_EXPORTS)
option(ARCHOERACX_CHINA_BUILD "Remove mainland-China download methods" OFF)
if(ARCHOERACX_CHINA_BUILD)
  target_compile_definitions(archoera_core PRIVATE ARCHOERACX_CHINA_BUILD=1)
endif()
target_link_libraries(archoera_core PRIVATE Threads::Threads ${CMAKE_DL_LIBS})

# Keep the shared library at the root of the build directory. With the
# documented configure command this resolves to
# app/build/native/libarchoera_core.so on Linux.
set(ARCHOERA_OUTPUT_DIR "${CMAKE_BINARY_DIR}")

set_target_properties(archoera_core PROPERTIES
  OUTPUT_NAME archoera_core
  LIBRARY_OUTPUT_DIRECTORY "${ARCHOERA_OUTPUT_DIR}"
  RUNTIME_OUTPUT_DIRECTORY "${ARCHOERA_OUTPUT_DIR}"
  ARCHIVE_OUTPUT_DIRECTORY "${ARCHOERA_OUTPUT_DIR}"
)

foreach(config IN ITEMS Debug Release RelWithDebInfo MinSizeRel)
  string(TOUPPER "${config}" config_upper)
  set_target_properties(archoera_core PROPERTIES
    LIBRARY_OUTPUT_DIRECTORY_${config_upper} "${ARCHOERA_OUTPUT_DIR}"
    RUNTIME_OUTPUT_DIRECTORY_${config_upper} "${ARCHOERA_OUTPUT_DIR}"
    ARCHIVE_OUTPUT_DIRECTORY_${config_upper} "${ARCHOERA_OUTPUT_DIR}"
  )
endforeach()

add_executable(abi_smoke tests/abi_smoke.c)
target_link_libraries(abi_smoke PRIVATE archoera_core)

set_target_properties(abi_smoke PROPERTIES
  C_STANDARD 11
  C_STANDARD_REQUIRED ON
  C_EXTENSIONS OFF
  RUNTIME_OUTPUT_DIRECTORY "${ARCHOERA_OUTPUT_DIR}"
  BUILD_RPATH "${ARCHOERA_OUTPUT_DIR}"
)

foreach(config IN ITEMS Debug Release RelWithDebInfo MinSizeRel)
  string(TOUPPER "${config}" config_upper)
  set_target_properties(abi_smoke PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY_${config_upper} "${ARCHOERA_OUTPUT_DIR}"
  )
endforeach()

add_executable(async_smoke tests/async_smoke.cpp)
target_link_libraries(async_smoke PRIVATE archoera_core Threads::Threads)

set_target_properties(async_smoke PROPERTIES
  CXX_STANDARD 20
  CXX_STANDARD_REQUIRED ON
  CXX_EXTENSIONS OFF
  RUNTIME_OUTPUT_DIRECTORY "${ARCHOERA_OUTPUT_DIR}"
  BUILD_RPATH "${ARCHOERA_OUTPUT_DIR}"
)

foreach(config IN ITEMS Debug Release RelWithDebInfo MinSizeRel)
  string(TOUPPER "${config}" config_upper)
  set_target_properties(async_smoke PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY_${config_upper} "${ARCHOERA_OUTPUT_DIR}"
  )
endforeach()

# Fake native module used as the test oracle for the module host. It is never
# linked into the core: it is loaded through the same dlopen path as a real
# module.
add_library(archoera_fake_module SHARED tests/fake_module.cpp)
target_compile_features(archoera_fake_module PRIVATE cxx_std_20)
target_include_directories(archoera_fake_module PRIVATE
  "${CMAKE_CURRENT_LIST_DIR}/../contracts/abi"
  "${CMAKE_CURRENT_LIST_DIR}/../contracts/module"
)
set_target_properties(archoera_fake_module PROPERTIES
  OUTPUT_NAME archoera_fake_module
  LIBRARY_OUTPUT_DIRECTORY "${ARCHOERA_OUTPUT_DIR}"
  RUNTIME_OUTPUT_DIRECTORY "${ARCHOERA_OUTPUT_DIR}"
  ARCHIVE_OUTPUT_DIRECTORY "${ARCHOERA_OUTPUT_DIR}"
)

foreach(config IN ITEMS Debug Release RelWithDebInfo MinSizeRel)
  string(TOUPPER "${config}" config_upper)
  set_target_properties(archoera_fake_module PROPERTIES
    LIBRARY_OUTPUT_DIRECTORY_${config_upper} "${ARCHOERA_OUTPUT_DIR}"
    RUNTIME_OUTPUT_DIRECTORY_${config_upper} "${ARCHOERA_OUTPUT_DIR}"
    ARCHIVE_OUTPUT_DIRECTORY_${config_upper} "${ARCHOERA_OUTPUT_DIR}"
  )
endforeach()

add_executable(module_smoke tests/module_smoke.cpp)
target_link_libraries(module_smoke PRIVATE archoera_core Threads::Threads)
add_dependencies(module_smoke archoera_fake_module)
target_compile_definitions(module_smoke PRIVATE
  "ARCHOERACX_FAKE_MODULE_PATH=\"$<TARGET_FILE:archoera_fake_module>\""
)

set_target_properties(module_smoke PROPERTIES
  CXX_STANDARD 20
  CXX_STANDARD_REQUIRED ON
  CXX_EXTENSIONS OFF
  RUNTIME_OUTPUT_DIRECTORY "${ARCHOERA_OUTPUT_DIR}"
  BUILD_RPATH "${ARCHOERA_OUTPUT_DIR}"
)

foreach(config IN ITEMS Debug Release RelWithDebInfo MinSizeRel)
  string(TOUPPER "${config}" config_upper)
  set_target_properties(module_smoke PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY_${config_upper} "${ARCHOERA_OUTPUT_DIR}"
  )
endforeach()
