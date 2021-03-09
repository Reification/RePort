# Copyright Reification Incorporated 2020

from Rhino import *
import rhinoscriptsyntax as rs
import scriptcontext as sc
import os
import shutil

import __plugin__

__commandname__ = "RePort"

Rhino_version = RhinoApp.Version.Major

RePort_version = __plugin__.version

# https://developer.rhino3d.com/api/RhinoCommon/html/T_Rhino_DocObjects_ObjectType.htm
# WARNING: Incomplete documentation for Rhino7 in RhinoScript reference:
# https://developer.rhino3d.com/api/rhinoscript/object_methods/objecttype.htm
#    - None                  0             Nothing.
#    X Point                 1             A point.
#    X PointSet              2             A point set or cloud.
#    X Curve                 4             A curve.
#    V Surface               8             A surface.
#    V Brep                  16            A brep.
#    V Mesh                  32            A mesh.
#    V Light                 256           A rendering light.
#    X Annotation            512           An annotation.
#    X InstanceDefinition    2048          A block definition.
#    V InstanceReference     4096          A block reference.
#    X TextDot               8192          A text dot.
#    X Grip                  16384         Selection filter value - not a real object type.
#    X Detail                32768         A detail.
#    X Hatch                 65536         A hatch.
#    X MorphControl          131072        A morph control.
#    V SubD                  262144        A SubD object.
#    X BrepLoop              524288        A brep loop.
#    X BrepVertex            1048576       a brep vertex.
#    X PolysrfFilter         2097152       Selection filter value - not a real object type.
#    X EdgeFilter            4194304       Selection filter value - not a real object type.
#    X PolyedgeFilter        8388608       Selection filter value - not a real object type.
#    X MeshVertex            16777216      A mesh vertex.
#    X MeshEdge              33554432      A mesh edge.
#    X MeshFace              67108864      A mesh face.
#    X Cage                  134217728     A cage.
#    X Phantom               268435456     A phantom object. https://discourse.mcneel.com/t/what-is-the-phantom-object-type/119363/7
#    X ClipPlane             536870912     A clipping plane.
#    V Extrusion             1073741824    An extrusion.
#    - AnyObject             4294967295    All bits set.
lights_export = 256 # Lights with configuration placeholders
meshes_export = 32  # Single export at fixed detail
detail_export = 8 + 16 + 262144 + 1073741824  # Multiple level of detail export
blocks_export = 4096  # Block instances are replaced by transform placeholders
export_select = lights_export | meshes_export | detail_export | blocks_export

# Select all objects that will be exported
def SelectExport():
    rs.ObjectsByType(geometry_type=export_select, select=True, state=0)

# Get selected objects that will be exported
def SelectedObjects():
    return rs.SelectedObjects(True, False)

# Remove unsafe characters from file name
# https://docs.microsoft.com/en-us/windows/win32/fileio/naming-a-file
# https://stackoverflow.com/questions/62771/how-do-i-check-if-a-given-string-is-a-legal-valid-file-name-under-windows
def SafeFileName(name):
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

# Remove unsafe characters from object name
def SafeObjectName(name):
    if name is None:
        return ""
    name = SafeFileName(name)
    name = name.replace("=", "-")
    return name

# QUESTION: Is it possible to also render block names safe?

# PROBLEM: Objects may have empty, non-unique opr unsafe names
# SOLUTION: Give each object a unique safe name, and create
# a dictionary to revert these changes after export.
def UniqueRename(name_map):
    selected = SelectedObjects()
    for object in selected:
        old_name = rs.ObjectName(object)
        new_name = SafeObjectName(old_name)
        if len(new_name) == 0:
            new_name = "Unknown"
            if rs.IsObject(object):
                new_name = "Object"
            # NOTE: IsObject and IsLight can both be true
            # so IsLight must be checked after
            if rs.IsLight(object):
                new_name = "Light"
        if old_name is None or len(old_name) == 0 or new_name in name_map:
            suffix = len(name_map)
            while new_name + "_" + str(suffix) in name_map:
                suffix += 1
            new_name += "_" + str(suffix)
        name_map[new_name] = old_name
        rs.ObjectName(object, new_name)

# Revert name changes
def RevertRename(name_map):
    selected = SelectedObjects()
    for object in selected:
        new_name = rs.ObjectName(object)
        if new_name in name_map:
            old_name = name_map[new_name]
            if old_name is None:
                old_name = ""  # Restores name is None state
            rs.ObjectName(object, old_name)
            del name_map[new_name]

# Rhino export looks at filename suffix to determine format
# Unity import looks for suffix to determined format,
# and at names before suffix to determine provenance.
def SaveSuffix():
    return ".3dm_" + str(Rhino_version) + ".fbx"

# IDEA: When exporting placeholders
# exclude materials and textures
# IDEA: When  exporting detail or placeholders exclude cameras and lights

# Save options target export for rendering
# NOTE: GeometryOnly=Yes would exclude BOTH cameras and lights
# NOTE: Unrecognized commands are interpreted as filename!
# When running Rhino5 entering Version=6 saves "Version=6.3dm"
def SaveOptions():
    # https://docs.mcneel.com/rhino/5/help/en-us/commands/save.htm
    options = "Version=" + str(Rhino_version) + " "\
        "SaveTextures=Yes "\
        "GeometryOnly=No "\
        "SavePluginData=No "\
        "SaveSmall=Yes "
    # https://docs.mcneel.com/rhino/6/help/en-us/commands/save.htm
    # https://docs.mcneel.com/rhino/7/help/en-us/commands/save.htm
    if Rhino_version >= 6:
        options += \
            "SaveNotes=No "
    return options

# FBX export options targeting Unity's import process
# NOTE: Enter exits fbx options
def fbx_options():
    # https://docs.mcneel.com/rhino/5/help/en-us/fileio/motionbuilder_fbx_import_export.htm
    options = \
        "ExportFileAs=Version7Binary "\
        "ExportNurbsObjectsAs=Mesh "\
        "ExportMaterialsAs=Lambert "
    # https://docs.mcneel.com/rhino/6/help/en-us/fileio/motionbuilder_fbx_import_export.htm
    if Rhino_version >= 6:
        options += \
            "YUp=No "
    # https://docs.mcneel.com/rhino/7/help/en-us/fileio/motionbuilder_fbx_import_export.htm
    if Rhino_version >= 7:
        options += \
            "ExportVertexNormals=Yes "\
            "ExportLights=Yes "\
            "ExportViews=No "
    return options

# Parametric Surface Meshing Options
# https://wiki.mcneel.com/rhino/meshsettings
# https://docs.mcneel.com/rhino/7/help/en-us/popup_moreinformation/polygon_mesh_detailed_options.htm
def meshing_options(detail):
    options = " PolygonDensity=0 "
    if detail == 0:
        options += "DetailedOptions "\
            "JaggedSeams=No "\
            "SimplePlane=Yes "\
            "Refine=Yes "\
            "PackTextures=No "
    else:
        options += "DetailedOptions "\
            "JaggedSeams=Yes "\
            "SimplePlane=Yes "\
            "Refine=Yes "\
            "PackTextures=No "
    if detail == 0:
        options += "AdvancedOptions "\
            "Angle=15 "\
            "AspectRatio=0 "\
            "Distance=0.01 "\
            "Density=0 "\
            "Grid=0 "\
            "MaxEdgeLength=0 "\
            "MinEdgeLength=0.001 "
        # SubD options: https://discourse.mcneel.com/t/exporting-subd-objects-to-fbx/119364/4
        if Rhino_version >= 7:
            options += \
                "SubdivisionLevel=5 "\
                "SubdivisionContext=Absolute "
    if detail == 1:
        options += "AdvancedOptions "\
            "Angle=30 "\
            "AspectRatio=0 "\
            "Distance=0.1 "\
            "Density=0 "\
            "Grid=0 "\
            "MaxEdgeLength=0 "\
            "MinEdgeLength=0.01 "
        # SubD options: https://discourse.mcneel.com/t/exporting-subd-objects-to-fbx/119364/4
        if Rhino_version >= 7:
            options += \
                "SubdivisionLevel=3 "\
                "SubdivisionContext=Adaptive "
    if detail == 2:
        options += "AdvancedOptions "\
            "Angle=45 "\
            "AspectRatio=0 "\
            "Distance=1.0 "\
            "Density=0 "\
            "Grid=0 "\
            "MaxEdgeLength=0 "\
            "MinEdgeLength=0.1 "
        # SubD options: https://discourse.mcneel.com/t/exporting-subd-objects-to-fbx/119364/4
        if Rhino_version >= 7:
            options += \
                "SubdivisionLevel=1 "\
                "SubdivisionContext=Adaptive "
    return options

# TODO: Adapt distance options to file units.
# IDEA: Adapt angle parameters (or maximum distance) to object size,
# so that large curves are not heavily segmented.

# NOTE: High levels of detail on large objects slows rendering,
# since rendered detail selection is proportionate to screen size.
# IDEA: If mesh edge size was consistent then it could be used
# to modify the screen size choice.

# NOTE: Least resolved detail level is used for collisions,
# so maximum distance cannot diverge too significantly.
# IDEA: Physics detail level should be based on object scale.

# IDEA: For terrain, a different export-import process would help.
# Export could be at maximum level of detail.
# Import could subdivide & subsample.
# Lightmap should be contributing only.

# IDEA: Interactive mode could allow modification of defaults
# Including count or even parameters.
# NOTE: This would require cached preferences.

# NOTE: file_name followed by space will exit save options
# IMPORTANT: enclosing file_name in " prevents truncation at spaces
def ExportModel(path, name, detail=0):
    file_name = os.path.join(path, name + SaveSuffix())
    return rs.Command(
        "-Export " +\
        SaveOptions() +\
        '"' + file_name + '" ' +\
        fbx_options() + "Enter " +\
        meshing_options(detail) + "Enter " +\
        "Enter", 
        True
    )

# NOTE: file_name followed by space will exit save options
# IMPORTANT: enclosing file_name in " prevents truncation at spaces
def ExportBlock(path, name, detail=0):
    file_name = os.path.join(path, name + save_sufix())
    return rs.Command(
        "-BlockManager Export " +\
        '"' + name + '" ' +\
        SaveOptions() +\
        '"' + file_name + '" ' +\
        fbx_options() + "Enter " +\
        meshing_options(detail) + "Enter " +\
        "Enter Enter",  # NOTE: Second enter exits BlockManager
        True
    )

# TODO: Find Documentation for custom units python interface
# https://developer.rhino3d.com/api/rhinoscript/document_methods/unitcustomunitsystem.htm

# Multiplier to convert model scale to meters
# FBX export will correctly scale models and blocks.
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

# Copy of unit basis vector
def UnitVector(b):
    vector = [0, 0, 0]
    vector[b] = 1
    return rs.CreateVector(vector)

# Copy of unit basis    
def UnitBasis():
    return [UnitVector(0), UnitVector(1), UnitVector(2)]

# Create a basis with direction as the final element
def BasisFromDirection(direction):
    b2 = rs.VectorUnitize(direction)
    for b in range(3):
        b0 = UnitVector(b)
        # At least one unit vector must meet this condition
        inner = rs.VectorDotProduct(b0, b2)
        if -0.5 < inner <= 0.5:
            b0 = rs.VectorUnitize(b0 - b2 * inner)
            b1 = rs.VectorCrossProduct(b2, b0)
            return [b0, b1, b2]
    return UnitBasis()

# Create a location encoding tetrahedron mesh
def LocationMesh(origin, basis):
    # Convert directions to positions relative to origin
    for b in range(3):
        basis[b] = rs.VectorAdd(basis[b], origin)
    # Construct basis tetrahedron
    mesh = rs.AddMesh(
        [origin, basis[0], basis[1], basis[2]],
        [[0, 2, 1], [0, 3, 2], [0, 1, 3], [1, 2, 3]]
    )
    return mesh

# Create a placeholder tetrahedron that encodes the block instance transform
# Units will be in meters to be consistent with import
def BlockLocation(object, scale):
    x = rs.BlockInstanceXform(object)
    p0 = [x.M00, x.M10, x.M20]  # X Basis direction
    p1 = [x.M01, x.M11, x.M21]  # Y Basis direction
    p2 = [x.M02, x.M12, x.M22]  # Z Basis direction
    p3 = [x.M03, x.M13, x.M23]  # Origin position
    # Rescale transform units
    for i in range(3):
        p0[i] /= scale
        p1[i] /= scale
        p2[i] /= scale
    # Construct basis tetrahedron
    placeholder = LocationMesh(p3, [p0, p1, p2])
    
    # Unity import will render names unique with a _N suffix on the N copy
    # so block name is included as a prefix to facilitate matching
    # in the case that block objects names are not unique
    block = rs.BlockInstanceName(object)
    block_name = SafeObjectName(block)
    object_name = rs.ObjectName(object)
    rs.ObjectName(placeholder, block_name + "=" + object_name)
    rs.ObjectLayer(placeholder, rs.ObjectLayer(object))
    return placeholder

# PROBLEM: Lights-only export fails!
# PROBLEM: Lights are exported without rotation or shape!
# SOLUTION: Create a placeholder tetrahedron that encodes light parameters
# - Rhino exports color, intensity and type
# - Placeholders will be created with corresponding names
# - Light type will be encoded in the name in case of unsupported types
# - Rectangles use X and Y scale for dimensions
# - Spots will use X (and equal Y) ratio to Z=1 for opening angle
# - Lines have a Y scale equal to the width
# - Range must be determined from context on import
# https://developer.rhino3d.com/api/rhinoscript/light_methods/light_methods.htm
def LightLocation(light, scale):
    if not rs.IsLight(light):
        return
    
    # Default light transform
    position = rs.LightLocation(light)
    direction = rs.LightDirection(light)
    basis = BasisFromDirection(direction)
    
    # Modify transform according to light type
    lightType = "UnknownLight"
    if rs.IsPointLight(light):
        lightType = "PointLight"
        #clone = rs.AddPointLight(position)
    if rs.IsDirectionalLight(light):
        lightType = "DirectionalLight"
        #clone = rs.AddDirectionalLight(position, position + direction)
    if rs.IsSpotLight(light):
        lightType = "SpotLight"
        outer = rs.SpotLightRadius(light)
        inner = rs.SpotLightHardness(light) * outer
        # Encode spot parameters in basis lengths
        basis = [
            basis[0] * outer,
            basis[1] * inner,
            direction
        ]
        #clone = rs.AddSpotLight(position + direction, outer, position)
        #rs.SpotLightHardness(clone, inner / outer)
    if rs.IsRectangularLight(light):
        # WARNING: Incomplete documentation for Rhino7 in RhinoScript reference:
        # https://developer.rhino3d.com/api/rhinoscript/light_methods/rectangularlightplane.htm
        lightType = "RectangularLight"
        quadVectors, quadLengths = rs.RectangularLightPlane(light)
        heightBasis = quadVectors[1] * quadLengths[0] / 2
        widthBasis = quadVectors[2] * quadLengths[1] / 2
        position = quadVectors[0] + heightBasis + widthBasis # center
        direction = -quadVectors[3] # negative of light direction
        # Encode quad dimensions in basis lengths
        basis = [
            widthBasis,
            -heightBasis,
            direction
        ]
        #corner = position - (widthBasis + heightBasis)
        #clone = rs.AddRectangularLight(corner, corner + widthBasis * 2, corner + heightBasis * 2)
    if rs.IsLinearLight(light):
        # Encode line segment in first basis
        lightType = "LinearLight"
        widthBasis = direction / 2
        position = position + widthBasis
        basis[2] = widthBasis
        #clone = rs.AddLinearLight (position - widthBasis, position + widthBasis)
    
    # Create scaled mesh
    for b in range(3):
        basis[b] /= scale
    placeholder = LocationMesh(position, basis)
    
    # NOTE: Lights have no corresponding exported block,
    # but the same notation will be used to configure lights in the exported model.
    # Unity import will render names unique with a _N suffix on the N copy
    # so block name is included as a prefix to facilitate matching
    # in the case that block instances names are not unique
    objectName = rs.ObjectName(light)
    rs.ObjectName(placeholder, lightType + "=" + objectName)
    rs.ObjectLayer(placeholder, rs.ObjectLayer(light))
    return placeholder

# Pause exporting to show additions and selection
def ShowStep(step_name):
    rs.EnableRedraw(True)
    input = rs.GetString("Showing step: " + step_name + " (Press Enter to continue)")
    rs.EnableRedraw(False)

# Export currently selected objects
# This enables recursive exporting of exploded block instances
# which creates detail and placeholder constituents for blocks
def ExportSelected(scale, path, name):
    #ShowStep("Scene or block export")
    # Include lights, exclude grips in selected
    selected = SelectedObjects()
    rs.UnselectAllObjects()
    export_exists = False
    
    # Export lights
    # NOTE: Lights must be exported separately so that
    # placeholder meshes can be imported without modification.
    placeholders = []
    for object in selected:
        if rs.ObjectType(object) & lights_export:
            rs.SelectObject(object)
            lightLocation = LightLocation(object, scale)
            placeholders.append(lightLocation)
            rs.SelectObject(lightLocation)
    if len(SelectedObjects()) > 0:
        #ShowStep("Light export")
        ExportModel(path, name + ".lights")
        rs.DeleteObjects(placeholders)
        export_exists = True
    rs.UnselectAllObjects()
    
    # Export meshes
    for object in selected:
        if rs.ObjectType(object) & meshes_export:
            rs.SelectObject(object)
    if len(SelectedObjects()) > 0:
        #ShowStep("Mesh objects export")
        ExportModel(path, name + ".meshes")
        export_exists = True
    rs.UnselectAllObjects()
    
    # Export detail
    for object in selected:
        if rs.ObjectType(object) & detail_export:
            rs.SelectObject(object)
    if len(SelectedObjects()) > 0:
        #ShowStep("Parametric objects export")
        ExportModel(path, name + ".meshes0", 0)
        ExportModel(path, name + ".meshes1", 1)
        ExportModel(path, name + ".meshes2", 2)
        export_exists = True
    rs.UnselectAllObjects()
    
    # Export blocks
    # NOTE: Block placeholders must be exported separately
    # so that meshes can be imported with modification.
    placeholders = []
    for object in selected:
        if rs.ObjectType(object) & blocks_export:
            # Export block constituents into subdirectory
            # On import contents of block will be merged,
            # and will then replace placeholders in scene and other blocks
            block = rs.BlockInstanceName(object)
            block_name = SafeObjectName(block)
            block_path = os.path.join(path, block_name)
            block_done = False
            try:
                os.mkdir(block_path)
            except OSError:
                # Directory exists so block has already been exported
                block_done = True
            if not block_done:
                # Export block instantiation
                instance = rs.InsertBlock(block, [0, 0, 0])
                # IMPORTANT: Nested instances are not exploded,
                # so that constituent blocks will be exported.
                instance_parts = rs.ExplodeBlockInstance(instance)
                rs.SelectObjects(instance_parts)
                block_name_map = {}
                UniqueRename(block_name_map)
                #ShowStep("Block " + block + " export")
                # IMPORTANT: block subdirectory is prepended to name
                # so that constituent blocks will be discovered or exported
                # in adjacent directories.
                # This prevents repeated exporting in nested directories.
                block_pathname = os.path.join(block_name, block_name)
                block_done = ExportSelected(scale, path, block_pathname)
                rs.DeleteObjects(instance_parts)
            if block_done:
                # Create a placeholder
                placeholders.append(BlockLocation(object, scale))
            else:
                # Remove empty directory
                os.rmdir(block_path)
    if len(placeholders) > 0:
        rs.SelectObjects(placeholders)
        #ShowStep("Block placeholder export")
        ExportModel(path, name + ".places")
        rs.DeleteObjects(placeholders)
        export_exists = True
    
    # Restore selection
    rs.SelectObjects(selected)
    return export_exists

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
        # BUG: If directory already exists os.mkdir may raise error.
        # NOTE: Directory deletion succeedes, and subsequent run
        # will not raise an error.
    return path, name

def RunCommand(is_interactive):
    command_preamble = __commandname__ + " v" + RePort_version
    if is_interactive:
        print(command_preamble)
    
    if not (Rhino_version == 7 or Rhino_version == 6 or Rhino_version == 5):
        print(command_preamble + ": does not support Rhino Rhino_version " + str(Rhino_version) + " -> abort")
        return
    
    path_name = GetExportPath(is_interactive)
    if path_name is None:
        print(command_preamble + ": no export location -> abort")
        return
    
    name_map = {}
    selected = rs.SelectedObjects(True, True)
    try:
        rs.EnableRedraw(False)
        
        # Select all exportable objects in scene
        SelectExport()
        UniqueRename(name_map)
        scale = ModelScale()
        ExportSelected(scale, *path_name)
    finally:
        # FIXME: Placeholders are not tracked for undo steps
        # TODO: Undo all script changes, including selection modifications
        # - failed execution will be cleaned
        # - successful execution will not appear to modify file
        # GOAL: Access Rhino's internal undo tracker so that the export
        # will be verified to have made no change to document.
        SelectExport()
        RevertRename(name_map)
        rs.UnselectAllObjects()
        rs.SelectObjects(selected)
        
        rs.EnableRedraw(True)
    print(command_preamble + ": success")

# GOAL: No changes to scene (no save request)
# GOAL: Launch Rhino in batch mode (headless) 
# with script, input & output paths as arguments
if __name__ == "__main__": RunCommand(False)
