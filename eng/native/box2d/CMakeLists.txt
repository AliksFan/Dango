cmake_minimum_required(VERSION 3.22)
cmake_policy(SET CMP0091 NEW)

project(dango_box2d_bridge LANGUAGES C)

if(NOT DEFINED BOX2D_SOURCE_DIR)
    message(FATAL_ERROR "BOX2D_SOURCE_DIR must point to the pinned official Box2D source tree")
endif()

set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(BOX2D_SAMPLES OFF CACHE BOOL "" FORCE)
set(BOX2D_BENCHMARKS OFF CACHE BOOL "" FORCE)
set(BOX2D_DOCS OFF CACHE BOOL "" FORCE)
set(BOX2D_UNIT_TESTS OFF CACHE BOOL "" FORCE)
set(BOX2D_VALIDATE ON CACHE BOOL "" FORCE)
set(BOX2D_COMPILE_WARNING_AS_ERROR OFF CACHE BOOL "" FORCE)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

add_subdirectory("${BOX2D_SOURCE_DIR}" box2d EXCLUDE_FROM_ALL)

add_library(dango_box2d SHARED dango_box2d_bridge.c)
add_library(dango_collision_review SHARED dango_collision_review.c dango_box2d_bridge.c)
target_link_libraries(dango_collision_review PRIVATE box2d::box2d)
target_compile_features(dango_collision_review PRIVATE c_std_17)
set_target_properties(dango_collision_review PROPERTIES PREFIX "")
add_library(dango_physics_module SHARED dango_box2d_bridge.c dango_collision_review.c)
target_link_libraries(dango_physics_module PRIVATE box2d::box2d)
target_compile_features(dango_physics_module PRIVATE c_std_17)
set_target_properties(dango_physics_module PROPERTIES PREFIX "" UNITY_BUILD ON UNITY_BUILD_BATCH_SIZE 2)
add_executable(dango_self_constraints_tests dango_self_constraints_tests.c)
add_executable(dango_spring_balance_tests dango_spring_balance_tests.c)
target_link_libraries(dango_spring_balance_tests PRIVATE box2d::box2d)
target_compile_features(dango_spring_balance_tests PRIVATE c_std_17)
target_link_libraries(dango_self_constraints_tests PRIVATE box2d::box2d)
target_compile_features(dango_self_constraints_tests PRIVATE c_std_17)
target_link_libraries(dango_box2d PRIVATE box2d::box2d)
target_compile_features(dango_box2d PRIVATE c_std_17)
target_compile_definitions(dango_box2d PRIVATE DANGO_BOX2D_BUILD)
set_target_properties(dango_box2d PROPERTIES
    OUTPUT_NAME "dango_box2d"
    PREFIX ""
    WINDOWS_EXPORT_ALL_SYMBOLS OFF
)

if(MSVC)
    target_compile_options(dango_collision_review PRIVATE /W4 /WX)
    target_compile_options(dango_physics_module PRIVATE /W4 /WX)
    target_compile_options(dango_spring_balance_tests PRIVATE /W4 /WX)
    target_compile_options(dango_self_constraints_tests PRIVATE /W4 /WX)
    target_compile_options(dango_box2d PRIVATE /W4 /WX)
    target_link_options(dango_box2d PRIVATE /Brepro)
    target_link_options(dango_physics_module PRIVATE /Brepro)
endif()
