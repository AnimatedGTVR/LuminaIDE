cmake_minimum_required(VERSION 3.20)
project(luminaterm CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(luminaterm SHARED terminal.cpp)
set_target_properties(luminaterm PROPERTIES CXX_VISIBILITY_PRESET hidden VISIBILITY_INLINES_HIDDEN ON)

if(MSVC)
  target_compile_options(luminaterm PRIVATE /W4)
else()
  target_compile_options(luminaterm PRIVATE -Wall -Wextra)
endif()

# Put the library where the C# project picks it up. A DLL is a RUNTIME artifact on Windows, and
# multi-config generators (Visual Studio) add a per-config sub-folder unless every variant is set.
if(LUMINA_OUT)
  foreach(cfg "" _DEBUG _RELEASE _RELWITHDEBINFO _MINSIZEREL)
    set_target_properties(luminaterm PROPERTIES
      LIBRARY_OUTPUT_DIRECTORY${cfg} "${LUMINA_OUT}"
      RUNTIME_OUTPUT_DIRECTORY${cfg} "${LUMINA_OUT}")
  endforeach()
endif()

if(WIN32)
  set_target_properties(luminaterm PROPERTIES PREFIX "")     # luminaterm.dll, not libluminaterm.dll
elseif(NOT APPLE)
  target_link_libraries(luminaterm PRIVATE util)               # forkpty lives in libutil on glibc
endif()
