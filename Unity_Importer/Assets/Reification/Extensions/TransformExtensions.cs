﻿// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Reification {
	public static class TransformExtensions {
		/// <summary>
		/// Array of all children of this transform
		/// </summary>
		/// <param name="recurse">false: immediate children; true: both immediate and descendant children</param>
		/// <remarks>
		/// If transform argument is null this yields all root Transforms in scene.
		/// The transform argument is not included in the list of children.
		/// </remarks>
		public static Transform[] Children(this Transform transform, bool recurse = false) {
			var childList = new List<Transform>();
			if(transform) {
				for(int c = 0; c < transform.childCount; ++c) childList.Add(transform.GetChild(c));
			} else {
				var rootGameObjectList = new List<GameObject>();
				Scene scene = SceneManager.GetActiveScene();
				scene.GetRootGameObjects(rootGameObjectList);

				for(int c = 0; c < rootGameObjectList.Count; ++c) childList.Add(rootGameObjectList[c].transform);
			}

			if(recurse) {
				var descendants = new List<Transform>();
				foreach(var child in childList) descendants.AddRange(child.Children(true));
				childList.AddRange(descendants);
			}

			return childList.ToArray();
		}

		public static GameObject[] Children(this GameObject gameObject, bool recurse = false) {
			var childTransforms = Children(gameObject?.transform, recurse);
			var childGameObjects = new GameObject[childTransforms.Length];
			for(var i = 0; i < childTransforms.Length; ++i) childGameObjects[i] = childTransforms[i].gameObject;
			return childGameObjects;
		}

		/// <summary>
		/// Transform lineage from root to transform, inclusive
		/// </summary>
		public static Transform[] Lineage(this Transform transform) {
			if(!transform) return new Transform[0];

			var lineage = new List<Transform>();
			var parent = transform;
			while(parent) {
				lineage.Add(parent);
				parent = parent.parent;
			}
			lineage.Reverse();
			return lineage.ToArray();
		}

		/// <summary>
		/// Closest parent in lineage of both lhs and rhs
		/// </summary>
		public static Transform SharedParent(this Transform lhs, Transform rhs) {
			if(!lhs || !rhs) return null;

			// TODO: Build paths to roots, then walk along both until there is a divergence.
			var lhsLineage = lhs.Lineage();
			var rhsLineage = rhs.Lineage();
			Transform sharedParent = null;
			for(int p = 0; p < lhsLineage.Length && p < rhsLineage.Length; ++p) {
				if(lhsLineage[p] != rhsLineage[p]) break;
				sharedParent = lhsLineage[p];
			}
			return sharedParent;
		}
	}
}
