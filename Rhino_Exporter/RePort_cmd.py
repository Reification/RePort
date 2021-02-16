# Copyright Reification Incorporated 2020

from Rhino import *
import rhinoscriptsyntax as rs
import scriptcontext as sc
import os
import shutil

__commandname__ = "RePort"

version = RhinoApp.Version.Major

# TODO: In interactive mode, limit selection to what is visible
# This would make it possible to break a model into separately
# imported components.

# IDEA: Running in interactive mode has an OPTION to select folder
# otherwise a folder will be created adjacent to doc, with same name.

# TODO: Not all blocks are used... restrict to ONLY export
# the models that are in use.

def ShowStep(step_name):
    rs.EnableRedraw(True)
    input = rs.GetString("Showing step: " + step_name + " (Press Enter to continue)")
    rs.EnableRedraw(False)

# Remove unsafe characters from file name
# https://docs.microsoft.com/en-us/windows/win32/fileio/naming-a-file
# https://stackoverflow.com/questions/62771/how-do-i-check-if-a-given-string-is-a-legal-valid-file-name-under-windows
def safe_file_name(name):
    name = name.replace('<', "")
    name = name.replace('>', "")
    name = name.replace(':', "")
    name = name.replace('"', "")
    name = name.replace('/', "")
    name = name.replace('\\', "")
    name = name.replace('|', "")
    name = name.replace('?', "")
    name = name.replace('*', "")
    return name

# Remove unsafe characters from block name
def safe_block_name(name):
    name = safe_file_name(name)
    name = name.replace("=", "-")
    return name

# Rhino export looks at filename suffix to determine format
# Unity import looks for suffix to determined format,
# and at names before suffix to determine provenance.
def save_suffix():
    return ".3dm_" + str(version) + ".fbx"

# IDEA: When exporting placeholders
# exclude materials and textures
# IDEA: When  exporting detail or placeholders exclude cameras and lights

# Save options target export for rendering
# NOTE: GeometryOnly=Yes would exclude BOTH cameras and lights
# NOTE: Unrecognized commands are interpreted as filename!
# When running Rhino5 entering Version=6 saves "Version=6.3dm"
def save_options():
    # https://docs.mcneel.com/rhino/6/help/en-us/commands/save.htm
    if version == 6:
        return \
            "Version=6 "\
            "SaveTextures=Yes "\
            "GeometryOnly=No "\
            "SavePluginData=No "\
            "SaveSmall=Yes "\
            "SaveNotes=No "
    # https://docs.mcneel.com/rhino/5/help/en-us/commands/save.htm
    if version == 5:
        return \
            "Version=5 "\
            "SaveTextures=Yes "\
            "GeometryOnly=No "\
            "SavePluginData=No "\
            "SaveSmall=Yes "
    raise Exception("Unsupported Rhino version: " + str(version))

# FBX export options targeting Unity's import process
# NOTE: Enter exits fbx options
def fbx_options():
    # https://docs.mcneel.com/rhino/6/help/en-us/fileio/motionbuilder_fbx_import_export.htm
    if version == 6:
        return \
            "ExportFileAs=Version7Binary "\
            "ExportNurbsObjectsAs=Mesh "\
            "ExportMaterialsAs=Lambert "\
            "YUp=No "
    # https://docs.mcneel.com/rhino/5/help/en-us/fileio/motionbuilder_fbx_import_export.htm
    if version == 5:
        return \
            "ExportFileAs=Version7Binary "\
            "ExportNurbsObjectsAs=Mesh "\
            "ExportMaterialsAs=Lambert "
    raise Exception("Unsupported Rhino version: " + str(version))

# TODO: Adapt distance options to file units
# IDEA: Interactive mode could allow modification of defaults

# Parametric Surface Meshing Options
# https://wiki.mcneel.com/rhino/meshsettings
def MeshingOptions(detail):
    meshing_options = " PolygonDensity=0 "
    if detail == 0:
        meshing_options += "DetailedOptions "\
            "JaggedSeams=No "\
            "SimplePlane=Yes "\
            "Refine=Yes "\
            "PackTextures=No "
    else:
        meshing_options += "DetailedOptions "\
            "JaggedSeams=Yes "\
            "SimplePlane=Yes "\
            "Refine=Yes "\
            "PackTextures=No "
    if detail == 0:
        meshing_options += "AdvancedOptions "\
            "Angle=15 "\
            "AspectRatio=0 "\
            "Distance=0.01 "\
            "Density=0 "\
            "Grid=0 "\
            "MaxEdgeLength=0 "\
            "MinEdgeLength=0.001 "
    if detail == 1:
        meshing_options += "AdvancedOptions "\
            "Angle=30 "\
            "AspectRatio=0 "\
            "Distance=0.1 "\
            "Density=0 "\
            "Grid=0 "\
            "MaxEdgeLength=0 "\
            "MinEdgeLength=0.01 "
    if detail == 2:
        meshing_options += "AdvancedOptions "\
            "Angle=45 "\
            "AspectRatio=0 "\
            "Distance=1.0 "\
            "Density=0 "\
            "Grid=0 "\
            "MaxEdgeLength=0 "\
            "MinEdgeLength=0.1 "
    return meshing_options

# NOTE: Detail level 2 is used for collisions,
# so maximum distance cannot diverge too significantly.
# IDEA: Physics detail level should be based on object scale.

# IDEA: For terrain, a different export-import process would help.
# Export could be at maximum level of detail.
# Import could subdivide & subsample.
# Lightmap should be contributing only.

# NOTE: file_name followed by space will exit save options
# IMPORTANT: enclosing file_name in " prevents truncation at spaces
def ExportModel(path, name, detail=0):
    file_name = os.path.join(path, name + save_suffix())
    rs.Command(
        "-Export " +\
        save_options() +\
        '"' + file_name + '" ' +\
        fbx_options() + "Enter " +\
        MeshingOptions(detail) + "Enter " +\
        "Enter", 
        True
    )

# NOTE: file_name followed by space will exit save options
# IMPORTANT: enclosing file_name in " prevents truncation at spaces
def ExportBlock(path, name, detail=0):
    file_name = os.path.join(path, name + save_sufix())
    rs.Command(
        "-BlockManager Export " +\
        '"' + name + '" ' +\
        save_options() +\
        '"' + file_name + '" ' +\
        fbx_options() + "Enter " +\
        MeshingOptions(detail) + "Enter " +\
        "Enter Enter",  # NOTE: Second enter exits BlockManager
        True
    )

# FIXME: Find Documentation for custom units python interface
# https://developer.rhino3d.com/api/rhinoscript/document_methods/unitcustomunitsystem.htm

# Multiplier to convert model scale to meters
# FBX import will correctly scale models, but 
# https://developer.rhino3d.com/api/rhinoscript/document_methods/unitsystem.htm
def ModelScale():
    meter = 1.0  # Unity units
    inch = meter * 0.3048 / 12.0
    units = int(sc.doc.ActiveDoc.ModelUnitSystem)
    scale = 1.0
    if units == 0: scale = 1.0 # None
    if units == 1: scale = meter * 1.0e-6
    if units == 2: scale = meter * 1.0e-3
    if units == 3: scale = meter * 1.0e-2
    if units == 4: scale = meter
    if units == 5: scale = meter * 1.0e+3
    if units == 6: scale = inch * 1.0e-6
    if units == 7: scale = inch * 1.0e-3
    if units == 8: scale = inch
    if units == 9: scale = inch * 12  # foot
    if units == 10: scale = inch * 12 * 5280  # mile
    if units == 11: 1.0 # Custom -- interface is  missing from python
    if units == 12: scale = meters * 1.0e-10
    if units == 13: scale = meter * 1.0e-9
    if units == 14: scale = meter * 1.0e-1
    if units == 15: scale = meter * 1.0e1
    if units == 16: scale = meter * 1.0e2
    if units == 17: scale = meter * 1.0e6
    if units == 18: scale = meter * 1.0e9
    if units == 19: scale = inch * 12 * 3  # yard
    if units == 20: scale = inch / 72  # printer point
    if units == 21: scale = inch / 6  # printer pica
    if units == 22: scale = meter * 1852 # nautical mile
    # https://en.wikipedia.org/wiki/Astronomical_unit
    if units == 23: scale = meter * 149597870700
    #  https://en.wikipedia.org/wiki/Light-year
    if units == 24: scale = meter * 9460730472580800
    # https://en.wikipedia.org/wiki/Parsec
    if units == 25: scale = meter * 149597870700 * 648000 / 3.14159265358979323
    return scale
    
# Create a placeholder tetrahedron that encodes the block instance transform
# Units will be in meters to be consistent with import
# https://developer.rhino3d.com/api/rhinoscript/document_methods/unitsystem.htm
def Placeholder(instance, scale):
    x = rs.BlockInstanceXform(instance)
    p0 = [x.M00, x.M10, x.M20]  # X Basis direction
    p1 = [x.M01, x.M11, x.M21]  # Y Basis direction
    p2 = [x.M02, x.M12, x.M22]  # Z Basis direction
    p3 = [x.M03, x.M13, x.M23]  # Origin position
    # Rescale mesh units
    for i in range(3):
        p0[i] /= scale
        p1[i] /= scale
        p2[i] /= scale
    # Convert directions to positions relative to origin
    for i in range(3):
        p0[i] += p3[i]
        p1[i] += p3[i]
        p2[i] += p3[i]
    # Construct basis tetrahedron
    placeholder = rs.AddMesh(
        [p3, p0, p1, p2],
        [[0, 2, 1], [0, 3, 2], [0, 1, 3], [1, 2, 3]]
    )
    # Unity import will render names unique with a _N suffix on the N copy
    # so block name is included as a prefix to facilitate matching
    objectName = rs.ObjectName(instance)
    if objectName is None:
        objectName = ""
    blockName = safe_block_name(rs.BlockInstanceName(instance))
    rs.ObjectName(placeholder, objectName + "=" + blockName)
    rs.ObjectLayer(placeholder, rs.ObjectLayer(instance))
    return placeholder

# https://developer.rhino3d.com/api/rhinoscript/selection_methods/objectsbytype.htm
#         Value        Description
#       - 0           All objects
#       X 1           Point
#       X 2           Point cloud
#       X 4           Curve
#       V 8           Surface or single-face brep -> Detail
#       V 16          Polysurface or multiple-face -> Detail
#       V 32          Mesh -> Single
#       V 256         Light -> Single
#       X 512         Annotation
#       V 4096        Instance or block reference -> Switch
#       X 8192        Text dot object
#       X 16384       Grip object (parametric control point)
#       X 32768       Detail (view placement on page)
#       X 65536       Hatch (surface coordinates)
#       X 131072      Morph control
#       X 134217728   Cage (deformation box)
#       X 268435456   Phantom (???)
#       X 536870912   Clipping plane (camera clipping plane)
#       V 1073741824  Extrusion (solid extrusion) -> Detail
single_export = 32 + 256  # Single export at fixed detail
detail_export = 8 + 16 + 1073741824  # Multiple level of deltail export
switch_export = 4096  # Block instances are switched with placeholders

# Export currently selected objects
# This enables recursive exporting of exploded block instances
def ExportSelected(scale, path, name):
    selected = rs.SelectedObjects()
    rs.UnselectAllObjects()
    export_exists = False
    
    # Export meshes
    for object in selected:
        if rs.ObjectType(object) & single_export:
            rs.SelectObject(object)
    if len(rs.SelectedObjects()) > 0:
        #ShowStep("Mesh & light objects export")
        ExportModel(path, name + ".meshes")
        export_exists = True
    rs.UnselectAllObjects()
    
    # Export detail
    for object in selected:
        if rs.ObjectType(object) & detail_export:
            rs.SelectObject(object)
    if len(rs.SelectedObjects()) > 0:
        #ShowStep("parametric objects export")
        ExportModel(path, name + ".detail2", 2)
        ExportModel(path, name + ".detail1", 1)
        ExportModel(path, name + ".detail0", 0)
        export_exists = True
    rs.UnselectAllObjects()
    
    # Export blocks
    placeholders = []
    for object in selected:
        if rs.ObjectType(object) & switch_export:
            # Export block constituents into subdirectory
            # On import contents of block will be merged,
            # and will then replace placeholders in scene and other blocks
            block = rs.BlockInstanceName(object)
            block_name = safe_block_name(block)
            block_path = os.path.join(path, block_name)
            block_done = False
            try:
                os.mkdir(block_path)
            except:
                # Block has already been exported
                block_done = True
            if not block_done:
                # Export block instantiation
                instance = rs.InsertBlock(block, [0, 0, 0])
                # IMPORTANT: Nested instances are not exploded,
                # so that constituent blocks will be exported.
                instance_parts = rs.ExplodeBlockInstance(instance)
                rs.SelectObjects(instance_parts)
                #ShowStep(block + " instance")
                # IMPORTANT: block subdirectory is prepended to name
                # so that constituent blocks will be discovered or exported
                # in adjacent directories.
                # This prevents repeated exporting in nested directories.
                block_pathname = os.path.join(block_name, block_name)
                block_done = ExportSelected(scale, path, block_pathname)
                rs.DeleteObjects(instance_parts)
            if block_done:
                # Create a placeholder
                placeholders.append(Placeholder(object, scale))
            else:
                # Remove empty directory
                os.rmdir(block_path)
    if len(placeholders) > 0:
        rs.SelectObjects(placeholders)
        #ShowStep("block instance replacements")
        ExportModel(path, name + ".places")
        rs.DeleteObjects(placeholders)
        export_exists = True
    
    # Restore selection
    rs.SelectObjects(selected)
    return export_exists

# TODO: Create interactive selection option
# Select all exportable objects in scene
def SelectScene(path, name):
    selected_types = single_export | detail_export | switch_export
    rs.ObjectsByType(geometry_type=selected_types, select=True, state=0)

# TODO: Handle a cancel during path selection without triggering a stack trace
# TODO: Make path selection interaction optional
# Default: create a folder next to active doc with the same name!
def GetExportPath(is_interactive):
    name = sc.doc.ActiveDoc.Name[:-4]  # Known safe
    path = sc.doc.ActiveDoc.Path[:-4]  # Known safe
    if name is None or path is None:
        print("Save document before exporting")
        return
    # NOTE: [:-4] removes ActiveDoc.Name suffix ".3dm"
    if is_interactive:
        path = rs.BrowseForFolder(
            folder=os.path.dirname(path),
            title="RePort",
            message="Choose root folder for exported files..."
        )  # Known safe
        if path is None:
            # User cancelled out of location selection
            return
    else:
        shutil.rmtree(path, True)
        os.mkdir(path)
    return path, name

# TODO: Find a way to not register scene changes

def RunCommand(is_interactive):
    if not (version == 5 or version == 6):
        print(__commandname__ + ": does not support Rhino version " + str(version) + " -> abort")
        return
    
    path_name = GetExportPath(is_interactive)
    if path_name is None:
        print(__commandname__ + ": no export location -> abort")
        return
    
    scale = ModelScale()
    try:
        rs.EnableRedraw(False)
        SelectScene(*path_name)
        ExportSelected(scale, *path_name)
    finally:
        rs.UnselectAllObjects()
        rs.EnableRedraw(True)
    print(__commandname__ + ": success")

# GOAL: No changes to scene (no save request)
# GOAL: Launch Rhino in batch mode (headless) 
# with script, input & output paths as arguments
if __name__ == "__main__": RunCommand(False)
