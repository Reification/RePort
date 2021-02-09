﻿// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Reification {
	public class MergeModels : MonoBehaviour {
		const string menuItemName = "Reification/Merge Models";
		const int menuItemPriority = 21;

		// IDEA: When this is applied to a directory instead of a GameObject
		// search the directory for prefabs to merge and create a merged output
		// This would make it easier for manual re-running.

		[MenuItem(menuItemName, validate = true, priority = menuItemPriority)]
		static private bool Validate() {
			if(Selection.gameObjects.Length < 2) return false;
			foreach(var gameObject in Selection.gameObjects) {
				var prefabAssetType = PrefabUtility.GetPrefabAssetType(gameObject);
				if(
					prefabAssetType == PrefabAssetType.MissingAsset ||
					prefabAssetType == PrefabAssetType.Model
				) return false;
			}
			return true;
		}

		[MenuItem(menuItemName, priority = menuItemPriority)]
		static private void Execute() {
			Undo.IncrementCurrentGroup();
			Undo.SetCurrentGroupName("Gather Assets");

			// Merge target is first selected GameObject
			var mergeTarget = Selection.activeGameObject;
			// Merge sources are subsequently selected GameObjects
			var mergeSources = new List<GameObject>();
			foreach(var gameObject in Selection.gameObjects) {
				if(gameObject == mergeTarget) continue;
				mergeSources.Add(gameObject);
			}
			ApplyTo(mergeTarget, mergeSources.ToArray());

			// View merged prefab
			var prefabType = PrefabUtility.GetPrefabAssetType(mergeTarget);
			if(
				prefabType == PrefabAssetType.Regular ||
				prefabType == PrefabAssetType.Variant
			) PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(mergeTarget));
		}

		static public void ApplyTo(GameObject mergeTarget, params GameObject[] mergeSources) {
			using(var editScope = new EP.EditGameObject(mergeTarget)) {
				var targetEdit = editScope.editObject;
				if(!targetEdit) return;
				foreach(var mergeSource in mergeSources) {
					using(var copyScope = new EP.CopyGameObject(mergeSource)) {
						var sourceCopy = copyScope.copyObject;

						// Unpack only root model prefab, constituent prefab links will be retained
						// NOTE: Applying UnpackPrefabInstance to a non-prefab object results in a crash
						if(PrefabUtility.GetPrefabAssetType(sourceCopy) != PrefabAssetType.NotAPrefab)
							PrefabUtility.UnpackPrefabInstance(sourceCopy, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
						Merge(sourceCopy.transform, targetEdit.transform);
					}
				}
			}
		}

		// Find shared start to names in nameList
		static public string MatchStart(params string[] nameList) {
			if(nameList.Length == 0) return "";
			if(nameList.Length == 1) return nameList[0];
			var match = "";
			for(var p = 0; p < nameList.Length; ++p) {
				var c = nameList[0][p];
				for(var n = 1; n < nameList.Length; ++n) {
					if(n >= nameList[n].Length) break;
					if(c != nameList[n][p]) break;
				}
				match += c;
			}
			return match;
		}

		// TODO: PathMatchStart to find save location.
		// TODO: These should all be moved elsewhere... maybe near SafeName?

		static public string NamesMatchStart(params GameObject[] gameObjects) {
			var nameList = new string[gameObjects.Length];
			for(var n = 0; n < gameObjects.Length; ++n) nameList[n] = gameObjects[n].name;
			return MatchStart(nameList);
		}

		// PROBLEM: If multiple intermediate levels of the hierarchy have the same name
		// then children will be randomly assigned. This could happen if Av0 has children,
		// Av1 does not and is added, but then Av2 has children, which could be parented to
		// either Av1 or Av0. Or, if the original model uses a name multiple times.

		static void Merge(Transform mergeFrom, Transform mergeTo) {
			// When names match, merge
			var mergeChildren = new List<Transform>();
			foreach(var childFrom in mergeFrom.Children()) {
				var childTo = mergeTo.NameFindInChildren(childFrom.name);
				if(childTo.Length == 0) {
					// ChildFrom is not in the hierarchy
					mergeChildren.Add(childFrom);
					continue;
				}
				if(PrefabUtility.GetPrefabAssetType(childFrom) != PrefabAssetType.NotAPrefab) {
					// ChildFrom is a Prefab and is already present in hierarchy since childTo != null
					continue;
				}
				if(childFrom.transform.childCount == 0) {
					// ChildFrom is a distinct instance even if childTo is present
					mergeChildren.Add(childFrom);
					continue;
				}
				// ChildFrom and ChildTo match, so merge children instead
				Merge(childFrom, childTo[0].transform);
			}
			foreach(var childFrom in mergeChildren) EP.SetParent(childFrom, mergeTo);
		}
	}
}
