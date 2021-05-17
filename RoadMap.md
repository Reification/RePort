RePort is a slice of [Reification's](https://reification.io) codebase. As we progress through the roadmap, additional parts of our private codebase will be made public.

We hope that this project will remove barries (both financial and technical) to experiencing architectural creations and ideas.

## v0.1.0 Alpha (private release)

## v0.1.1 Alpha (public release) - 8 Feb 2021
* Reification namespace
* RePort code organization
* RePort folder created automatically
* Single import opens scene
* Public project

## v0.2.0 - Rhino5 Support - 18 Feb 2021
* Identify model exporter from file name
* Extensible importer support
* Models can be reimported and will recreate deleted assets
* Press enter/return to illuminate dark spaces
* Scroll to adjust height, press scroll to reset height
* Add light source & height adjustment to player
* Extensible handling of imported files by provenance

## v0.2.1 - Rhino7 Support - 26 Feb 2021
* Rhino7 support
* Rhino plugin confirms version when used
* Meshes are extracted from models

## v0.3.0 - Light Sources - 23 Mar 2021
* Rhino light sources are imported with correct orientation and shape
* Visible sources are created for all lights
* Gain and bloom adjustments make light sources appear luminous
* Optional "good" alternative to "fast" lighting generation

## v0.3.1 - Landscape Importing - 18 May 2021
* Enable importing without scene creation
* Lightmap UVs are generated only for static LOD0 meshes
* Create placeholders for missing prefabs to enable reaplcement
* Import process for large terrain
* Import process for replaced vegetation assets
* Added light probe proxies for lower levels of detail
* Rhino linear lights are approximated using Unity area lights
* Original model files are removed to prevent reimporting
* Exported models use enclosing folder name instead of document name
* Height adjustment by scrolling or when holding Alt/Opt

## v0.3.2 - Lower Detail Optimizations
* Conditional proxy volume updates
* Area-dependent detail thresholds
* Identify normalmap textures during import

## v0.3.3 - Progress Bars & Performance
* Import progress indictor for entire import process
* Mesh modification progress indicator
* Prevent OS window lock-up during drag-and-drop import

## v0.4.0 - General Transforms
* Support for general linear transforms
* Model recentering
* Mesh standardizing

## v1.0.0 - Reification Build Request
* Upload configured scenes for code-signed multi-user deployment

## v1.0.1 - Tutorials
* Tutorial Video
* Example Rhino project

## Will you be adding support for other CAD/BIM applications?
Yes. Applications will be added to the roadmap when support plans are finalized. If you would like to see a specific application supported, [please let us know.](mailto:support@reification.io)

## Will you be adding support for ray tracing?
Our first priority is supporting realtime global illumination. Unity has announced that their 2021.2 release will add support for Enlighten in the HDRP, which we are currently testing.

## Will you be adding support for mobile?
We are evaluating solutions that will enable mobile support without compromising visual fidelity.
