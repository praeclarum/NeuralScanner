cmake_minimum_required (VERSION 3.3)
project (OpenGR-Apps LANGUAGES CXX)


find_package(OpenMP)
set(OpenGRAppsDeps)
if(OpenMP_CXX_FOUND)
    set(OpenGRAppsDeps OpenMP::OpenMP_CXX)
    add_definitions(-DOpenGR_USE_OPENMP)
    message(STATUS "Parallelization enabled using OpenMP")
endif()


if(OpenGR_COMPILE_APPS)
    add_subdirectory(io)
    add_subdirectory(Super4PCS)
    add_subdirectory(PCLWrapper)
    add_subdirectory(ExtPointBinding)
endif(OpenGR_COMPILE_APPS)
