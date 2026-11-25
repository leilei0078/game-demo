#include "ozz/animation/runtime/blending_job.h"
#include "ozz/base/maths/soa_transform.h"

extern "C" int FarmerMask(const float* base, const float* carry, float weight, float* output) {
  using namespace ozz::math;
  using Job = ozz::animation::BlendingJob;
  auto pose = [](const float* values) {
    auto result = SoaTransform::identity();
    result.rotation = {simd_float4::Load1(values[0]), simd_float4::Load1(values[1]),
                       simd_float4::Load1(values[2]), simd_float4::Load1(values[3])};
    return result;
  };
  SoaTransform lower[] = {pose(base)}, upper[] = {pose(carry)}, result[1];
  SimdFloat4 lower_mask[] = {simd_float4::Load1(1.f-weight)}, upper_mask[] = {simd_float4::Load1(weight)};
  Job::Layer layers[2]; layers[0].weight = layers[1].weight = 1.f;
  layers[0].transform = lower; layers[1].transform = upper;
  layers[0].joint_weights = lower_mask; layers[1].joint_weights = upper_mask;
  Job job; job.rest_pose=lower; job.layers=layers; job.output=result;
  if (!job.Run()) return 0;
  const auto& rotation=result[0].rotation;
  output[0]=GetX(rotation.x); output[1]=GetX(rotation.y); output[2]=GetX(rotation.z); output[3]=GetX(rotation.w);
  return 1;
}
