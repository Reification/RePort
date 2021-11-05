// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using UnityEngine;
using  UnityEditor;

namespace Reification {
	/// <summary>
	/// Import only non-executing package contents
	/// </summary>
	/// <remarks>
	///	Imported packages can contain code that will execute immediately,
	/// making unknown package importing fundamentally unsafe.
	/// 
	/// This import process extracts all code from a package,
	/// leaving only assets that are known to be safe.
	/// WARNING: This process can produce missing references
	/// in the imported objects.
	///
	/// WARNING: This code relies on tar being available via the OS CLI
	/// </remarks>
	[InitializeOnLoad]
	public class SafePackageImport {
		// Batch execution from command line
		public const string cliFlagName = "-importPackage-safe";
		
		// PROBLEM: paths must be absolute, since Unity is not executed as login user
		// QUESTION: how are paths with spaces passed as arguments?

		// QUESTION: If command line arguments are given to interactive editor
		// will reimporting occur whenever code is recompiled?
		
		static SafePackageImport() {
			// Check for package to import
			// WARNING: This constructor is called 2 times during Editor launch
			var arguments = Environment.GetCommandLineArgs();
			UnityEngine.Debug.Log("SafePackageImport: " + String.Join(" ", arguments));
			for (var i = 0; i < arguments.Length - 1; ++i) {
				if (arguments[i] != cliFlagName) continue;
				var packageFile = arguments[i + 1];
				// PROBLEM: SafePackageImport() may be executed multiple times during launch (uncomment Debug.Log to verify)
				// SOLUTION: Register package and process to be invoked by editor
				importPackageFiles.Add(packageFile);
				if (importPackageFiles.Count == 1) EditorApplication.update += ProcessPackagesImport;
			}
		}

		// Interactive execution from menu
		const string menuItemName = "Assets/Import Package/Make Safe Package...";
		const int menuItemPriority = 0;
		
		[MenuItem(menuItemName, priority = menuItemPriority)]
		static private void Execute() {
			// Select unsafe package file
			var unsafePackageFile = EditorUtility.OpenFilePanel("Load Unsafe Package", "", "unitypackage").Replace('/', Path.DirectorySeparatorChar);
			if(unsafePackageFile == null || unsafePackageFile.Length == 0) return;

			// Declare safe package file
			var lastSeparatorIndex = unsafePackageFile.LastIndexOf(Path.DirectorySeparatorChar);
			var packagePath = unsafePackageFile.Substring(0, lastSeparatorIndex);
			var packageName = unsafePackageFile.Substring(lastSeparatorIndex + 1);
			packageName = packageName.Substring(0, packageName.LastIndexOf('.')) + ".safe";
			var safePackageFile = EditorUtility.SaveFilePanel("Save Safe Package", packagePath, packageName, "unitypackage").Replace('/', Path.DirectorySeparatorChar);
			if(safePackageFile == null || safePackageFile.Length == 0) return;

			MakeSafePackage(unsafePackageFile, safePackageFile);
		}
		
		static HashSet<string> importPackageFiles = new HashSet<string>();
		
		static void ProcessPackagesImport() {
			EditorApplication.update -= ProcessPackagesImport;
			if(importPackageFiles.Count == 0) return;

			// NOTE: Multiple packages can be imported using command line
			// OPTION: Import in order and break on first failure
			// OPTION: Continue importing even if some packages fail
			// IDEA: Look for comma separated values to identify sequential packages.
			try {
				foreach(var unsafePackageFile in importPackageFiles) {
					if (!File.Exists(unsafePackageFile)) {
						UnityEngine.Debug.LogWarning($"{cliFlagName} {unsafePackageFile} does not exist!");
						continue;
					}

					// Make package safe to import
					UnityEngine.Debug.Log($"Safe Package Import: {unsafePackageFile}");
					var safePackageFile = unsafePackageFile.Substring(0, unsafePackageFile.LastIndexOf('.')) + ".safe.unitypackage";
					var removedAssets = MakeSafePackage(unsafePackageFile, safePackageFile);
					if (removedAssets.Count > 0) {
						UnityEngine.Debug.Log(
							$"SafePackageImport {unsafePackageFile} removed assets:\n" +
							String.Join(",\n- ", removedAssets)
						);
					}
					
					// Import package
					AssetDatabase.ImportPackage(safePackageFile, !Application.isBatchMode);
				}
			} catch(System.Exception e) {
				UnityEngine.Debug.LogError($"ProcessPackagesImport failed with error:\n{e.Message}");
				return;
			} finally {
				// IMPORTANT: Unblock future imports even if this import failed
				importPackageFiles.Clear();
			}
		}
		
		/// <summary>
		/// Extract a tar+gzip file into a directory
		/// </summary>
		/// <param name="packageFile">Package to extract</param>
		/// <param name="extractPath">Directory to create, if different from package name</param>
		public static void Extract(string packageFile, string extractPath = null) {
			var suffix = packageFile.Substring(0, packageFile.LastIndexOf('.'));
			if(extractPath == null) extractPath = packageFile.Substring(0, packageFile.Length - suffix.Length);
			
			// Create directory if it does not exist
			// NOTE: tar does not create directories
			if(Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
			Directory.CreateDirectory(extractPath);

			// Extract using tar (available on command line of all platforms)
#if UNITY_EDITOR_WIN
			var startInfo = new ProcessStartInfo(
				"cmd", 
				$"/c 'tar -xf {packageFile} -C {extractPath}'"
			);
#endif
#if UNITY_EDITOR_OSX
			var startInfo = new ProcessStartInfo(
				"/bin/bash", 
				$"-c 'tar -xf {packageFile} -C {extractPath}'"
			);
#endif
#if UNITY_EDITOR_LINUX
			var startInfo = new ProcessStartInfo(
				"/bin/bash", 
				$"-c 'tar -xf {packageFile} -C {extractPath}'"
			);
#endif
			startInfo.RedirectStandardOutput = false;
			startInfo.UseShellExecute = false;
			startInfo.CreateNoWindow = true;

			var process = new System.Diagnostics.Process();
			process.StartInfo = startInfo;
			process.Start();
			process.WaitForExit();
		}

		/// <summary>
		/// Package directory into a tar+gzip file
		/// </summary>
		/// <param name="packageFile">Package to create</param>
		/// <param name="extractPath">Directory to compress, if different from package name</param>
		public static void Package(string packageFile, string extractPath = null) {
			var suffix = packageFile.Substring(packageFile.LastIndexOf('.'));
			if (extractPath == null) extractPath = packageFile.Substring(0, packageFile.Length - suffix.Length);

			// Package using tar (available on command line of all platforms)
			// NOTE: .unitypackage files use tar + gzip
			// https://www.howtogeek.com/248780/how-to-compress-and-extract-files-using-the-tar-command-on-linux/
#if UNITY_EDITOR_WIN
			var startInfo = new ProcessStartInfo(
				"cmd", 
				$"/c 'tar -czf {packageFile} -C {extractPath} .'"
			);
#endif
#if UNITY_EDITOR_OSX
			var startInfo = new ProcessStartInfo(
				"/bin/bash", 
				$"-c 'tar -czf {packageFile} -C {extractPath} .'"
			);
#endif
#if UNITY_EDITOR_LINUX
			var startInfo = new ProcessStartInfo(
				"/bin/bash",
				$"-c 'tar -czf {packageFile} -C {extractPath} .'"
			);
#endif
			startInfo.RedirectStandardOutput = false;
			startInfo.UseShellExecute = false;
			startInfo.CreateNoWindow = true;

			var process = new System.Diagnostics.Process();
			process.StartInfo = startInfo;
			process.Start();
			process.WaitForExit();
		}
		
		public static HashSet<string> safeAssets { get; } = new HashSet<string>{
			// Unity data
			".meta",
			".wlt",
			".mat",
			".anim",
			".unity",
			".prefab",
			".physicsMaterial2D",
			".physicMaterial",
			".controller",
			".lighting",
			".giparams",
			".asset",

			// Models: https://docs.unity3d.com/Manual/3D-formats.html
			".fbx",
			".dae",
			".dxf",
			".obj",

			// Videos: https://docs.unity3d.com/Manual/VideoSources-FileCompatibility.html
			".asf",
			".avi",
			".dv",
			".m4v",
			".mov",
			".mp4",
			".mpg",
			".mpeg",
			".ogv",
			".vp8",
			".webm",
			".wmv",

			// Images: https://docs.unity3d.com/Manual/ImportingTextures.html
			".bmp",
			".exr",
			".gif",
			".hdr",
			".iff",
			".jpg",
			".pict",
			".png",
			".psd",
			".tga",
			".tiff",
			".cubemap",

			// Sounds: https://docs.unity3d.com/Manual/AudioFiles.html
			".mp3",
			".ogg",
			".wav",
			".aif",
			".aiff",
			".mod",
			".it",
			".s3m",
			".xm",
		};

		/// <summary>
		/// Make a safe version of a package
		/// </summary>
		/// <remarks>
		/// This includes only assets from Assets/ subdirectory,
		/// so all assets in Packages/ are ignored.
		/// </remarks>
		/// <returns>List of removed assets</returns>
		public static List<string> MakeSafePackage(string unsafePackageFile, string safePackageFile = null) {
			if(safePackageFile == null) safePackageFile = unsafePackageFile;
			var extractPath = safePackageFile.Substring(0, safePackageFile.LastIndexOf('.'));

			// Extract package contents
			Extract(unsafePackageFile, extractPath);
			
			// Remove unsafe items
			var removedAssets = new List<string>();
			// The searchPattern argument matches only file names, not paths
			// https://docs.microsoft.com/en-us/dotnet/api/system.io.directory.getfiles
			// NOTE: Files may be modified, so EnumerateFiles is not used.
			var pathNameFiles = Directory.GetFiles(
				extractPath,
				"pathname",
				SearchOption.AllDirectories
			);
			foreach(var pathNameFile in pathNameFiles) {
				var pathName = File.ReadAllText(pathNameFile);
				if(
					pathName.StartsWith("Assets/") &&
					safeAssets.Contains(pathName.Substring(pathName.LastIndexOf('.')))
				) continue;

				UnityEngine.Debug.Log($"Removed package asset: {pathName}");
				removedAssets.Add(pathName);
				var assetPath = pathNameFile.Substring(0, pathNameFile.LastIndexOf(Path.DirectorySeparatorChar));
				Directory.Delete(assetPath, true);
			}
			
			// Create safe version of package
			Package(safePackageFile, extractPath);
			
			// Remove package directory
			Directory.Delete(extractPath, true);

			return removedAssets;
		}
	}
}
