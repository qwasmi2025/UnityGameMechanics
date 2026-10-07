# Changelog
All notable changes to this package will be documented in this file.
The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/) and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [0.10.4] - 2026-05-24

### Fixed
- Fixed obsolete material import warnings for models in Unity 6.4.

## [0.10.3] - 2026-04-06

### New
- Added SetBoneReadWriteStatus method to the GPUICrowdInstance component, allowing you to modify the read/write status of bones on a per-instance basis.
- Added bone indexes next to the bone names under Bone Settings, making it easier to identify each bone's assigned index.

### Fixed
- Fixed obsolete method warnings in Unity 6.4.

## [0.10.2] - 2026-02-27

### Fixed
- Fixed an issue where vertex data was generated multiple times when the same Crowd Rig was used across multiple prototypes.

## [0.10.1] - 2026-02-17

### Fixed
- Fixed a shader warning in the MaterialVariations demo scene on Unity 6.3.

## [0.10.0] - 2026-01-10

### New
- Animation clips can now be optionally pre-baked and saved as a bytes asset.
- Optimized the animation baking process for improved performance.
- Added methods to clear old buffer data, preventing stale animation data from affecting runtime behavior.
- Added a UI label under the Baked Clips list on the Crowd Rig to display the size of each baked animation clip.
- Added a method to CrowdInstance to check whether the crowd animator is in transition.

### Fixed
- Fixed a bounding box calculation error when the root bone has a positional and rotational offset.

## [0.9.5] - 2025-12-12

### New
- Added additional methods to retrieve animator data using a clip reference or a clip name.

## [0.9.4] - 2025-10-30

### New
- Added BakeAnimationClips methods to the GPUICrowdAPI for pre-baking animation clips.

### Changed
- Commonly used GPUI components are now automatically disabled on unsupported platforms.

## [0.9.3] - 2025-10-02

### Fixed
- Resolved an issue where multiple skinned mesh renderers with different bind poses on the same model caused skinning errors.

## [0.9.2] - 2025-10-01

### New
- Added a stackable shader to integrate Crowd Animations with Better Shaders.

## [0.9.1] - 2025-09-05

### Changed
- Modified CrowdBatchComputePlayer to start animations only for new instances when adding them, instead of restarting all instances.

### Fixed
- Resolved an issue where bone transform writing continued after an instance was removed.

## [0.9.0] - 2025-08-25

### New
- Optimized SetPass calls for better performance.
- Added Optional Renderers demo scene.

### Fixed
- Resolved the "AnimationEvent on animation has no receiver." error message that occurred when baking animation clips with events.
- Fixed an issue where changes to the Crowd Instance component in Prefab Mode were not being saved.

## [0.8.5] - 2025-08-12

### New
- Added a new Shader Graph setup node variant that includes only the GPU skinning setup without the procedural instancing configuration.
- Added 'Is Crowd Animations' checkbox to the Material Variations definition.
- Added more detailed logs and documentation links related to shader setup.
- Added Material Variations demo scene.

### Fixed
- Resolved issue where the "Enable Crowd Animations" option could not be used on a prefab variant if the CrowdInstance component was present on the base prefab but removed from the variant.

## [0.8.4] - 2025-07-31

### New
- Added SetAnimationSpeeds API methods.

### Fixed
- Fixed issue where animations did not stop when speed was set to zero.

## [0.8.3] - 2025-07-01

### Fixed
- Resolved an issue where the Mecanim Reader failed to play animations when using Blend Trees.

## [0.8.2] - 2025-06-29

### Fixed
- Resolved an issue where the Mecanim Reader ignored the animation looping setting.

## [0.8.1] - 2025-06-21

### Fixed
- Fixed demo package paths when importing demos from the Samples section of the Package Manager.

## [0.8.0] - 2025-06-20

### New
- Initial release.