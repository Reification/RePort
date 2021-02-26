RePort is a slice of [Reification's](https://reification.io) codebase. As we progress through the roadmap, additional parts of our private codebase will be made public.

We hope that this project will remove barries (both financial and technical) to experiencing architectural creations and ideas.

Presently, the project only includes a custom exporter for Rhino. Other exporters will be added as opportunity permits.

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
* Add light source & height adjustment to player
* Extensible handling of imported files by provenance

## v0.2.1 - Rhino7 Support - 26 Feb 2021
* Rhino7 support
* Rhino plugin confirms version when used
* Meshes are extracted from models

## v0.3.0 Semantic Importing
* Interactive export process for vegetation
* Optimized import process for vegetation
* Allow passage through objects named "door" or "window"
* Make materials shiney if they are named "glass" or "metal"
* Extensible import stages

## v0.3.1 - Progress Bars & Performance
* Import progress indictors for entire process
* Mesh modification progress pop-up
* Identify normalmap textures during import
* Prevent OS window lock-up during drag-and-drop import

## v0.4.0 - Interior Lighting
* Auto light instensity adjustment
* Auto terrain lightmap parameters
* Auto vegetation replacement

## v0.4.1 - General Transforms
* Support for general linear transforms
* Model recentering
* Mesh standardizing
* Simplify model transform hierarchy

## v1.0.0 - Reification Build Request
* Upload configured scenes for code-signed multi-user deployment

## v1.0.1 - Tutorials
* Example Rhino project
* Scripting Documentation
* Tutorial Video

## Will you be adding support for other CAD/BIM applications?
Yes. Applications will be added to the roadmap when support plans are finalized. If you would like to see a specific application supported, [please let us know.](mailto:support@reification.io)

## Why does RePort use deprecated lighting and XR systems?
In both of these cases Unity has chosen to deprecate a system without providing an equally capable alternative. In the case of Enlighten, the built in alternative (Unity’s Progressive lightmapper) will not update bounced lighting as the sun moves. In the case of the Virtual Reality SDKs, the replacement (XR Plugin Management) does not support SteamVR. We will be assessing new solutions as they become available.
