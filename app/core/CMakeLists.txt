find_package(Threads REQUIRED)

# System appearance backend: native OS APIs only, no polling in Dart.
set(ARCHOERA_APPEARANCE_SOURCES src/platform_appearance_stub.cpp)
set(ARCHOERA_SYSTEM_SOURCES src/platform_system_stub.cpp)
set(ARCHOERA_STATUS_SOURCES src/platform_status_stub.cpp)
set(ARCHOERA_GPU_SOURCES src/platform_gpu_stub.cpp)
if(WIN32)
  set(ARCHOERA_APPEARANCE_SOURCES src/platform_appearance_windows.cpp)
  set(ARCHOERA_SYSTEM_SOURCES src/platform_system_windows.cpp)
  set(ARCHOERA_STATUS_SOURCES src/platform_status_windows.cpp)
  set(ARCHOERA_GPU_SOURCES src/platform_gpu_windows.cpp)
elseif(APPLE)
  enable_language(OBJCXX)
  set(ARCHOERA_APPEARANCE_SOURCES src/platform_appearance_macos.mm)
  set(ARCHOERA_SYSTEM_SOURCES src/platform_system_macos.mm)
  set(ARCHOERA_STATUS_SOURCES src/platform_status_macos.cpp)
  set_source_files_properties(src/platform_appearance_macos.mm
    src/platform_system_macos.mm PROPERTIES COMPILE_FLAGS "-fobjc-arc")
else()
  find_package(PkgConfig QUIET)
  if(PkgConfig_FOUND)
    pkg_check_modules(DBUS QUIET dbus-1)
  endif()
  if(DBUS_FOUND)
    set(ARCHOERA_APPEARANCE_SOURCES src/platform_appearance_linux.cpp)
    set(ARCHOERA_SYSTEM_SOURCES src/platform_system_linux.cpp)
  endif()
  # /proc + statvfs need no extra dependency.
  set(ARCHOERA_STATUS_SOURCES src/platform_status_linux.cpp)
  # /sys/class/drm needs no extra dependency either.
  set(ARCHOERA_GPU_SOURCES src/platform_gpu_linux.cpp)
endif()

add_library(archoera_core SHARED
  src/archoera_core.cpp
  src/method_registry.cpp
  src/module_host.cpp
  src/module_registry.cpp
  src/core_json.cpp
  src/core_result.cpp
  src/core_tasks.cpp
  src/core_status.cpp
  src/core_actions.cpp
  src/core_appearance.cpp
  src/core_module_bridge.cpp
  src/platform_system_common.cpp
  src/platform_status_common.cpp
  ${ARCHOERA_APPEARANCE_SOURCES}
  ${ARCHOERA_SYSTEM_SOURCES}
  ${ARCHOERA_STATUS_SOURCES}
  ${ARCHOERA_GPU_SOURCES}
)

target_compile_features(archoera_core PRIVATE cxx_std_20)
target_include_directories(archoera_core
  PUBLIC "${CMAKE_CURRENT_LIST_DIR}/../contracts/abi"
  PRIVATE "${CMAKE_CURRENT_LIST_DIR}/../contracts/module"
)
target_compile_definitions(archoera_core PRIVATE ARCHOERA_CORE_EXPORTS)
target_link_libraries(archoera_core PRIVATE Threads::Threads ${CMAKE_DL_LIBS})

# Keep every non-ABI symbol inside the .so: the ARCHOERA_API macro re-opens
# visibility for the ten exported functions.
set_target_properties(archoera_core PROPERTIES
  C_VISIBILITY_PRESET hidden
  CXX_VISIBILITY_PRESET hidden
  VISIBILITY_INLINES_HIDDEN ON
)

if(WIN32)
  target_link_libraries(archoera_core PRIVATE dwmapi advapi32 shell32)
elseif(APPLE)
  target_link_libraries(archoera_core PRIVATE
    "-framework AppKit" "-framework Foundation" "-framework UserNotifications")
  set_target_properties(archoera_core PROPERTIES OBJCXX_STANDARD 20)
elseif(DBUS_FOUND)
  target_include_directories(archoera_core PRIVATE ${DBUS_INCLUDE_DIRS})
  target_link_libraries(archoera_core PRIVATE ${DBUS_LINK_LIBRARIES})
  target_compile_definitions(archoera_core PRIVATE ARCHOERA_HAVE_DBUS=1)
endif()

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

# Queue-cap policy test. It only uses the header-inline enqueue helpers, so it
# does not link the shared library (whose internals are hidden).
add_executable(event_queue_smoke tests/event_queue_smoke.cpp)
target_compile_features(event_queue_smoke PRIVATE cxx_std_20)
target_include_directories(event_queue_smoke PRIVATE
  "${CMAKE_CURRENT_LIST_DIR}/src"
  "${CMAKE_CURRENT_LIST_DIR}/../contracts/abi"
  "${CMAKE_CURRENT_LIST_DIR}/../contracts/module"
)
set_target_properties(event_queue_smoke PROPERTIES
  CXX_STANDARD 20
  CXX_STANDARD_REQUIRED ON
  CXX_EXTENSIONS OFF
  RUNTIME_OUTPUT_DIRECTORY "${ARCHOERA_OUTPUT_DIR}"
)
foreach(config IN ITEMS Debug Release RelWithDebInfo MinSizeRel)
  string(TOUPPER "${config}" config_upper)
  set_target_properties(event_queue_smoke PROPERTIES
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
