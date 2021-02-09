// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System.Collections.Generic;
using UnityEngine;

namespace Reification {
	public static class PathNameExtensions {
		/// <returns>the PathName identifying this GameObject</returns>
		public static PathName Path(this GameObject gameObject) {
			return new PathName(gameObject);
		}

		/// <returns>the PathName identifying this GameObject</returns>
		public static PathName Path(this Component component) {
			return new PathName(component?.gameObject);
		}

		/// <returns>an array of all GameObject matching name that are children of parent</returns>
		/// <remarks>
		/// Unlike Transform.Find() path separators are assumed to be a part of the name.
		/// When parent = null the search begins with the scene root.
		/// </remarks>
		public static GameObject[] NameFind(PathName name, Transform parent = null, bool recurse = false) {
			List<GameObject> findList = new List<GameObject>();
			Transform[] childList = TransformExtensions.Children(parent);
			foreach(var child in childList) {
				if(name == child.name) findList.Add(child.gameObject);
				if(recurse) findList.AddRange(NameFind(name, child, recurse));
			}
			return findList.ToArray();
		}

		/// <returns>an array of all child GameObjects matching name</returns>
		public static GameObject[] NameFindInChildren(this Component component, PathName name, bool recurse = false) {
			return NameFind(name, component?.transform, recurse);
		}

		/// <returns>an array of all child GameObjects matching name</returns>
		public static GameObject[] NameFindInChildren(this GameObject gameObject, PathName name, bool recurse = false) {
			return NameFind(name, gameObject?.transform, recurse);
		}

		/// <returns>an array of all GameObjects identified by path from parent</returns>
		/// <remarks>
		/// When parent = null the search begins with the scene root.
		/// </remarks>
		public static GameObject[] PathFind(string path, Transform parent = null) {
			var pathName = new PathName(path, PathName.PathStep.StepType.Path);
			var transformList = pathName.Find(parent);
			var gameObjectList = new List<GameObject>();
			foreach(var transform in transformList) gameObjectList.Add(transform.gameObject);
			return gameObjectList.ToArray();
		}

		/// <returns>an array of all GameObjects identified by path from this</returns>
		public static GameObject[] PathFindInChildren(this Component component, string pathName) {
			return PathFind(pathName, component?.transform);
		}

		/// <returns>an array of all GameObjects identified by path from this</returns>
		public static GameObject[] PathFindInChildren(this GameObject gameObject, string pathName) {
			return PathFind(pathName, gameObject?.transform);
		}

		/// <summary>
		/// Returns all instances matching pathName
		/// </summary>
		/// <param name="pathName">is interpreted as either a path or a name</param>
		/// <returns></returns>
		/// <remarks>
		/// Counterpart to GameObject.Find(string) this returns all matching instances, instead of one.
		/// Names beginning with '/' indicate objects at the root of the scene.
		/// Names including '/' will be traversed as a path beginning from the first matched object.
		/// </remarks>
		public static GameObject[] FindAll(string pathName) {
			// PROBLEM: Actually, empty names are allowed...
			// Empty object names are not allowed
			if(pathName.Length == 0) return new GameObject[0];

			// This behavior is consistent with GameObject.Find()
			if(pathName[0] != '/') return NameFind(pathName);

			// Search by path
			// IMPORTANT: PathName names begin with the name of a GameObject, so "/" is dropped
			var path = new PathName(pathName.Substring(1), PathName.PathStep.StepType.Path);

			var transformList = path.Find();
			var gameObjectList = new List<GameObject>();
			foreach(var transform in transformList) gameObjectList.Add(transform.gameObject);
			return gameObjectList.ToArray();
		}
	}
}
