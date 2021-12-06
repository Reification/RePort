// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

/// <summary>
/// Create UnityPackage for RePort module
/// </summary>
public class RePort_Package {
	const string menuItemName = "Reification/Products/RePort Package";
	const int menuItemPriority = 200;

	public const string packageName = "RePort_for_Unity";

	public static HashSet<string> assetPaths { get; } = new HashSet<string>{
			// Import
			"Assets/Reification/AutoImport/Editor/RePort.cs",
			"Assets/Reification/AutoImport/Editor/MergeModels.cs",
			"Assets/Reification/AutoImport/Editor/GatherAssets.cs",
			"Assets/Reification/AutoImport/Editor/ReplacePrefabs.cs",
			"Assets/Reification/AutoImport/Editor/AutoLOD.cs",
			"Assets/Reification/AutoImport/Editor/AutoStatic.cs",
			"Assets/Reification/AutoImport/Editor/AutoColliders.cs",
			"Assets/Reification/AutoImport/Editor/AutoScene.cs",
			"Assets/Reification/AutoImport/Editor/AutoLightSources.cs",
			"Assets/Reification/AutoImport/Shaders/StandardCrossfade.shader",
			
			// Lighting Bake
			// TODO: Separate this
			"Assets/Reification/AutoImport/Editor/AutoLightCharts.cs",
			"Assets/Reification/AutoImport/Editor/AutoLightDirect.cs",
			"Assets/Reification/AutoImport/Editor/AutoLightProbes.cs",
			"Assets/Reification/AutoImport/Editor/AutoLightmaps.cs",
			"Assets/Reification/AutoImport/Editor/Importers/RhinoImporters.cs",
			"Assets/Reification/AutoImport/Scripts/LightProbeProxyUpdate.cs",
			"Assets/Reification/AutoImport/Settings/FastLightmaps.giparams",
			"Assets/Reification/AutoImport/Settings/GoodLightmaps.giparams",

			// Player
			"Assets/Reification/AutoImport/Scripts/DragCamera.cs",
			"Assets/Reification/AutoImport/Scripts/MovePlayer.cs",
			"Assets/Reification/AutoImport/Scripts/ScrollHeight.cs",
			"Assets/Reification/AutoImport/Scripts/SunColor.cs",
			"Assets/Reification/AutoImport/Scripts/SunOrbit.cs",
			"Assets/Reification/AutoImport/Scripts/KeyToggleActive.cs",
			"Assets/Reification/AutoImport/Scripts/KeyToggleEnabled.cs",
			"Assets/Reification/AutoImport/Prefabs/Player.prefab",
			"Assets/Reification/AutoImport/Prefabs/Sun.prefab",
			"Assets/Reification/AutoImport/Settings/PostProcessing.asset",
			// TODO: Declare PostProcessing package dependency

			// Mesh Repair
			"Assets/Reification/MeshRepair/MeshExtensions.cs",
			"Assets/Reification/MeshRepair/Editor/MeshMaterialize.cs",
			"Assets/Reification/MeshRepair/Editor/MeshInvert.cs",
			"Assets/Reification/MeshRepair/Editor/MeshAddBackSide.cs",
			"Assets/Reification/MeshRepair/Editor/MeshGetTopSide.cs",

			// General
			"Assets/Reification/EditorPlayer/EditorPlayer.cs",
			"Assets/Reification/Extensions/TransformExtensions.cs",
			"Assets/Reification/Extensions/PathName/PathName.cs",
			"Assets/Reification/Extensions/PathName/PathNameExtensions.cs",
			
			"Assets/Reification/CloudTasks"
	};

	[MenuItem(menuItemName, priority = menuItemPriority)]
	private static void Execute() {
		// Validate package contents
		foreach(var path in assetPaths) {
			var guid = AssetDatabase.AssetPathToGUID(path);
			if(guid == null || guid.Length == 0) {
				Debug.LogError($"RePort_Package: missing asset: {path} -> abort");
				return;
			}
		}

		// Ensure that Builds directory exists
		Directory.CreateDirectory(Path.Combine(Application.dataPath, "..", "Builds"));

		// Export package
		var package = new string[assetPaths.Count];
		assetPaths.CopyTo(package);
		var fileName = (Application.dataPath + "/../Builds/" + packageName + ".unitypackage").Replace('/', Path.DirectorySeparatorChar);
		AssetDatabase.ExportPackage(package, fileName, ExportPackageOptions.Recurse);
		Debug.Log("Created: " + fileName);
	}
}
