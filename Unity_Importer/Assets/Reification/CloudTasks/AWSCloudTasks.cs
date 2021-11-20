using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Reification.CloudTasks {
	public class AWSCloudTasks {
		// SECURITY: WARNING: This is stored in plain text
		// it would be better if it integrated with OS secrets manager
		public const string configFile = "AWSCloudTasks/config.json";

		public AWSCloudTasks() {
			// Load config file
			
			// Log in to Cognito user pool
			// Get credentials from Cognito identity pool

			// Load bake and build queues
			// Schedule periodic checking
		}

		// Upload model package for baking
		public void UploadBake() {
			// S3 object upload (multi-part?)
			// Enqueue periodic checking for result
		}

		public void DownloadBake() {
			// S3 object download (multi-part?)
			// Check for exists
			// If exists download
			// After download remove object
			// Clear queue
			// Initiate import

			// NOTE: Downloaded files will persist...
			// Since import might fail, upload and download
			// files should be managed with a cache size
			// and recency prioritization
		}

		// Upload model package for building as bundles
		public void UploadBuild() {
			// NOTE: Application needs to be able to access these builds
		}

		// Configure access to builds
		public void BuildAccess() {
			// Default state is private in which the model can only be accessed by owner.
			
			// Permanent users can be added
			// Users can be granted moderator permissions (owner is a moderator)
			// - Invite users
			// - Convert users to permanent
			// - Revoke user access
			// - Start / stop public access (while moderator is present)
			// - Reset model
			// Owner has additional privileges
			// - Promote user to moderator
			// - View & edit full access list
			// - Create / Update / Remove model
			// NOTE: User invitations can be created using time windows
			// with future start and stop times.

			// NOTE: Accessing a model is defined by a downloaded bundle
			// and a server address. The server itself additionally has access
			// to a user configuration.
			// NOTE: Updating a model takes an existing bundle and server,
			// copies them to the access configuration, and removes the old configuration.
			// The server inherits the previous user configuration.
			
			// IMPORTANT: It must be possible to create a guest list
			// including access times, and to receive a list with access
			// permissions to share with those user.
		}
	}
}
