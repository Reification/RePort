using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Reification.CloudTasks.AWS {[InitializeOnLoad]
	public class AWSCloudTasksEditor {
		const string menuItemName = "Reification/TEST AWS Cloud Tasks";
		const int menuItemPriority = 100;

		[MenuItem(menuItemName, priority = menuItemPriority)]
		private static void Execute() {
			if(
				cloudTasks == null && 
				!Enable()
			) return;
			
			// TODO: Create a package of a scene in cacheFolder/Bake/
			
			var localList = Directory.GetFiles(Path.Combine(Application.persistentDataPath, AWSCloudTasks.cacheFolder, "Bake"));
			if(localList.Length > 0) {
				var localPath = localList[0];
				Debug.Log($"Starting bake for {localPath}");
				cloudTasks.InitiateBake(localPath);
			}
		}
		
		static AWSCloudTasksEditor() {
			Enable();
		}

		private static AWSCloudTasks cloudTasks;

		// Enroll to periodically check for downloads
		public static bool Enable() {
			// Authenticate
			cloudTasks = new AWSCloudTasks();
			if(!cloudTasks.authenticated) {
				cloudTasks = null;
				return false;
			}
			
			EditorApplication.update += Update;
			return true;
		}

		public static double doneCheckPeriod = 60.0; // seconds
		
		static double lastTimeSinceStartup = 0.0;
		
		static void Update() {
			var nextTimeSinceStartup = EditorApplication.timeSinceStartup;
			if(doneCheckPeriod > nextTimeSinceStartup - lastTimeSinceStartup) return;
			lastTimeSinceStartup = nextTimeSinceStartup;
			
			cloudTasks.RetrieveBake((string localPath) => {
				Debug.Log($"SUCCESS downloaded baked file: {localPath}");
				// TODO: SafePackageImport will be called here
				return true;
			});
		}

		public static void Disable() {
			cloudTasks = null;
			EditorApplication.update += Update;
		}
	}
}
