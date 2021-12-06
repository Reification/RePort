// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Reification.CloudTasks.AWS {
	[InitializeOnLoad]
	public class AWSEditor : AssetPostprocessor {
		const string menuItemName = "Reification/CloudTasks/AWS Bake";
		const int menuItemPriority = 100;
		
		[MenuItem(menuItemName, validate = true, priority = menuItemPriority)]
		private static bool Validate() {
			return cognito != null && cognito.authenticated;
		}
		
		[MenuItem(menuItemName, priority = menuItemPriority)]
		private static void Execute() {
			// Package only the active scene - multi scene baking is not yet supported
			var scenePath = EditorSceneManager.GetActiveScene().path;
			var sceneRoot = scenePath.Substring(0, scenePath.LastIndexOf('.'));
			var sceneName = sceneRoot.Substring(sceneRoot.LastIndexOf('/') + 1);
			sceneName = S3.SafeS3Name(sceneName);
			
			var packageRoot = Path.Combine(Application.persistentDataPath, S3.localCache, "Bake");
			Directory.CreateDirectory(packageRoot);
			var packagePath = Path.Combine(packageRoot, sceneName + ".unitypackage");
			AssetDatabase.ExportPackage(
				new string[] {scenePath}, 
				packagePath,
				ExportPackageOptions.IncludeDependencies
			);
			
			if(!PushBake(packagePath)) {
				Debug.LogWarning($"Unable to start bake for {packagePath}");
				return;
			}
			Debug.Log($"Starting bake for {packagePath}");
		}
		
		// Enable importing of AWS account using drag & drop onto Unity editor
		static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths) {
			foreach(var assetPath in importedAssets) {
				var assetName = assetPath.Substring(assetPath.LastIndexOf('/') + 1);
				if(assetName != accountFile) continue;
				
				var projectRoot = Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length);
				projectRoot = projectRoot.Replace('/', Path.DirectorySeparatorChar);
				
				var projectAccountPath = Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
				File.Copy(projectAccountPath, cognito.accountPath, true);
				AssetDatabase.DeleteAsset(assetPath);
				Debug.Log($"AWS account moved to: {cognito.accountPath}");
				if(cognito.authenticated) Debug.Log("AWS account authorized!");
				else Debug.LogWarning("AWS account authorization failed");
			}
		}
		
		/// <summary>
		/// Name of AWS account file used to authenticate
		/// </summary>
		public const string accountFile = "CloudTasks_AWSAccount.json";

		private static Cognito cognito;

		private static S3 s3;
		
		static AWSEditor() {
			// NOTE: Application.persistentDataPath has the form base_path/Company/Project/*
			// so a project independent path requires moving up two levels.
			var projectDirectory = new DirectoryInfo(Application.persistentDataPath);
			var accountRoot = projectDirectory.Parent.Parent.ToString();
			var accountPath = Path.Combine(accountRoot, "Reification", accountFile);
			
			cognito = new Cognito(accountPath);
			s3 = new S3(cognito);
			
			// Enable by default in order to check for completed tasks from previous sessions
			EditorApplication.update += Update;
		}
		
		// TODO: User account files should be imported using drag & drop
		// The files can be identified by suffix, verified and then copied to the correct location.
		// NOTE: If credentials already exist conflict should be handled without data loss.

		public static double doneCheckPeriod = 60.0; // seconds (0 or less disables checking)
		
		static double lastTimeSinceStartup = 0.0;
		
		static void Update() {
			if(doneCheckPeriod <= 0.0) return;
			
			var nextTimeSinceStartup = EditorApplication.timeSinceStartup;
			if(nextTimeSinceStartup - lastTimeSinceStartup < doneCheckPeriod) return;
			lastTimeSinceStartup = nextTimeSinceStartup;
			
			PullBakeDone();
		}
		
		// TODO: In the case of an error, a log file is found instead of a package file.
		// After downloading the log, files will be deleted from the server, but retained locally.
		// The user will be requested to contact reification for support.

		// NOTE: If a user is working on multiple projects on different machines
		// they might have a single account (single email) used by both.
		// In the present configuration the only machine that receives the build is the one that uploaded it.
		// NOTE: If the /Bake-Done/ package persists, it could be retrieved by other machines.
		// IDEA: Client only looks for items in outbox, and cleans up everything when the build succeeds.
		// NOTE: This also allows for the option of a bake/build failure report.

		public static bool PushBake(string localPath) {
			if(!cognito.authenticated) return false;
			
			var fileName = localPath.Substring(localPath.LastIndexOf(Path.DirectorySeparatorChar) + 1);
			// EXPECT: S3 policy restricts access to a folder named for the account identity
			var cloudPath = $"{cognito.identity}/Bake/{fileName}";
			if(!s3.PutFile(localPath, cloudPath)) return false;

			// IMPORTANT: Client could upload a new file version while old one is processing.
			// This will overwrite the file version, but could result in an output race, so
			// processors and products must be found and deleted BEFORE a new processing task starts.

			return true;
		}

		// Check for completed bake tasks, download and import if done
		// WARNING: Importing may be slow, or may even wait for user input
		// so a periodic call must halt until each call completes.
		public static bool PullBakeDone() {
			var localBakeRoot = Path.Combine(Application.persistentDataPath, S3.localCache, "Bake");
			if(!Directory.Exists(localBakeRoot)) return true;
			
			// Check for expected files
			var expectedFileNames = new HashSet<string>();
			foreach(var filePath in Directory.GetFiles(localBakeRoot)) {
				// TODO: Add report file suffix as well, to retrieve report if error occurred
				// TODO: Build should ALWAYS upload log, even in the case of success
				// and client should ALWAYS download log, since problems may be found later
				var fileName = filePath.Substring(filePath.LastIndexOf(Path.DirectorySeparatorChar) + 1);
				expectedFileNames.Add(fileName);
			}
			if(expectedFileNames.Count == 0) return true;

			if(!cognito.authenticated) return false;
			try {
				// Disable updating to prevent race in case of slow download or import
				EditorApplication.update -= Update;
				
				// Check for completed files
				// EXPECT: S3 policy restricts access to a folder named for the account identity
				var localBakeDoneRoot = Path.Combine(Application.persistentDataPath, S3.localCache, "Bake-Done");
				var cloudBakeRoot = $"{cognito.identity}/Bake/";
				var cloudBakeDoneRoot = $"{cognito.identity}/Bake-Done/";
				if(!s3.ListFiles(cloudBakeDoneRoot, out var cloudList)) return false;
				foreach(var cloudPath in cloudList) {
					var fileName = cloudPath.Substring(cloudPath.LastIndexOf('/') + 1);
					if(!expectedFileNames.Contains(fileName)) continue;

						var localBakeDonePath = Path.Combine(localBakeDoneRoot, fileName);
						if(!s3.GetFile(cloudPath, localBakeDonePath)) return false;
						Debug.Log($"Downloaded baked file: {localBakeDonePath}");
						// QUESTION: What should be done if file cannot be downloaded?
						// TODO: Move local file to bug-report folder, create report, attempt to delete remote file.

						// TODO: If an error report was received instead of a package, handle that case.
						// Download console log, notify users, retain original package & remove /Bake/ package
						// OPTION: Notification invites sharing with Reification for diagnostics.

						// SECURITY: IMPORTANT: Even if server is compromised, executable code will be stripped
						SafePackageImport.ImportPackage(localBakeDonePath, false, SafePackageImport.KeepPackages.Unsafe);
						Debug.Log($"Imported baked file: {localBakeDonePath}");

						// IDEA: Retain original unbaked package for reversion
						// and baked package (unsafe) for diagnostics
						// NOTE: This requires checking folder sizes & file dates to avoid large caches
						
						// Clean up queues
						File.Delete(Path.Combine(localBakeRoot, fileName)); // Prevent download and reimport
						s3.DeleteFile($"{cloudBakeRoot}/{fileName}"); // Will not retry bake
						File.Delete(localBakeDonePath); // NOTE: Could keep this file in order to share
						s3.DeleteFile(cloudPath); // NOTE: Baked package could persist for use in a build without re-uploading
						Debug.Log($"Removed bake files");
				}
			} finally {
				// Resume updating
				EditorApplication.update += Update;
			}

			return true;
		}

		// IMPORTANT: Server ip address may change. However, the bucket and path containing bundles
		// will not change, so this can be used to discover the current servers and verify bundles hashes.
		
		// QUESTION: How can user access be managed? Giving a user, or a group, access to new models by default
		// would be helpful.

		// Upload model package for building as bundles
		public static void UploadBuild(string buildPath) {
			// PutFile (or use existing)
			// This causes platform-specific bundles to be created,
			// after which a server will be launched, and the server address will be recorded.
			// If an existing build name is used the existing server must either reload or restart with a new address.
			
			// The required additional data is the access policy for users.
			// If no policy is provided a policy granting access only to the client will be created.
			// QUESTION: Can file access be controlled using the same user id as server connection?
			
			// OBSERVATION: If baked packages persist on the server, there would be no need
			// to upload or copy the file the previously downloaded file - the existing file could be used.
			
			// OBSERVATION: The same race condition exists here as for UploadBake.
		}

		public static void DeleteBuild(string buildPath) {
			// Delete build files and terminate the associated server
		}

		// PROBLEM: UpdateAccess needs to extracted to be runtime-accessible
		// so that access can be granted or revoked at runtime
		// IDEA: The server can make use of AWS runtime services, so
		// all that is required is to indicate to the server that AWSRuntime should be used.
		
		// Configure access to builds
		public static void UpdateAccess(string buildPath) {
			// Access has two parts: Server and Policy
			// The minimal configuration is a running server
			// that is accessible only by the owner.
			// The owner can create access credentials
			// for other users, and can force the server
			// to reload credentials.

			// Permanent users can be added
			// Users can be granted moderator permissions (owner is a moderator)
			// - Invite users
			// - Convert users to permanent
			// - Revoke user access
			// - Reset model
			// - Start / stop public access (while moderator is present)
			// Owner has additional privileges
			// - Promote user to moderator
			// - View & edit full access list
			// - Create / Update / Remove model
			// NOTE: User invitations can be created using time windows
			// with future start and stop times.
			// NOTE: In the case of timed access it should still be possible
			// to download the model before but NOT after the access window.
			// OPTION: Public access without a moderator is not allowed.
			// OPTION: Public access without a moderator warns all users before connecting.

			// NOTE: Accessing a model is defined by a downloaded bundle
			// and a server address. The server itself additionally has access
			// to a user configuration.
			// NOTE: Updating a model takes an existing bundle and server,
			// copies them to the access configuration, and removes the old configuration.
			// The server inherits the previous user configuration.
			// IMPORTANT: It must be possible to check if a downloaded model is current.
			
			// IMPORTANT: It must be possible to create a guest list
			// including access times, and to receive a list with access
			// permissions to share with those user.
		}
		
		// IMPORTANT: User avatars will need a similar process, with file identity being shared
		// to all clients connected to a server.
	}
}
