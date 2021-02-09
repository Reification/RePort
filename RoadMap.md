RePort is a slice of [Reification's](https://reification.io) codebase. As we progress through the roadmap, additional parts of our private codebase will be made public.

We hope that this project will remove barries (both financial and technical) to experiencing architectural creations and ideas.

Presently, the project only includes a custom exporter for Rhino. Other exporters will be added as opportunity permits.

## v0.1.0 Alpha (private release)

## v0.1.1 Alpha (public release)
* Reification namespace
* RePort code organization
* RePort folder created automatically
* Single import opens scene
* Public project

## v0.2.0 - Model Provenance
* Identify model exporter from file name
* Rhino5 model rotation
* Places basis remapping

## v0.2.1 - Progress Bars & Performance
* Prevent Windows lock-up during drag & drop import
* Import progress pop-up
* Mesh modification progress pop-up
* Rhino exporter button
* BUGFIX: Rewriting open deleted scene blocks import

## v0.2.2 - Semantic Importing
* Allow passage through objects named "door" or "window"
* Make materials shiney if they are named "glass" or "metal"

## v0.2.3 - Reification Build Request
* Upload configured scenes for code-signed multi-user deployment

## v0.3.0 - Interior Lighting
* Auto light instensity adjustment
* Auto terrain lightmap parameters
* Auto vegetation replacement

## v0.3.1 - General Transforms
* Support for general linear transforms
* Model recentering
* Mesh standardizing
* Simplify model transform hierarchy

## v0.3.2 - Tutorial
* Example Rhino project
* Scripting Docs
* Tutorial Video
* Single-action from Rhino
* Single-action Export & Import

## Why does RePort use deprecated lighting and XR systems?
In both of these cases Unity has chosen to deprecate a system without providing an equally capable alternative. In the case of Enlighten, the built in alternative (Unity’s Progressive lightmapper) will not update bounced lighting as the sun moves. In the case of the Virtual Reality SDKs, the replacement (XR Plugin Management) does not support SteamVR. We will be assessing new solutions as they become available.
