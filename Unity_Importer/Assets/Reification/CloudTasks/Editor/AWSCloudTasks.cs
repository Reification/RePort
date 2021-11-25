﻿using System;
using System.IO;
using System.Net.Http;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using UnityEngine;
using Unity.Plastic.Newtonsoft.Json;
using Unity.Plastic.Newtonsoft.Json.Linq;

// NOTE: Scene bundle access is required at runtime.
// GOAL: AWS service is not the only way to get private data
// IDEA: Files can be requested if name is known, but names cannot be listed.
// File names will have random nonces, making the request effectively secure.
// NOTE: Accessing a private server requires unique credentials, while downloading
// requires only a universal file identifier. However, the credentials can be used
// to request the file identity, which may be changed.
// NOTE: User avatars will need a similar process, with file identity being shared
// to all clients connected to a server.

// TEMP: Remove from editor folder after testing to enable runtime use.
using UnityEditor;

namespace Reification.CloudTasks {
	public class AWSCloudTasks {
		const string menuItemName = "Reification/TEST AWS Cloud Tasks";
		const int menuItemPriority = 100;

		[MenuItem(menuItemName, priority = menuItemPriority)]
		private static void Execute() {
			var test = new AWSCloudTasks();
		}
		
		private static readonly HttpClient httpClient = new HttpClient();
		
		public const string accountFile = "AWSCloudTasks_account.json";

		// SECURITY: WARNING: Account is stored in plain text
		// Regions: https://docs.aws.amazon.com/general/latest/gr/cognito_identity.html
		[Serializable]
		private class Account {
			public string username;
			public string password;
			public string identity;
			public string userpool; // Cognito User Pool
			public string clientId; // Cognito User Pool Client App Id
			public string policyId; // Cognito Identity Pool Id
			public string cloudUrl; // S3 bucket domain
		}

		private Account account = null;

		private void LoadAccount() {
			// FIXME: persistentDataPath is project-specific, but this should be universal
			// NOTE: persistentDataPath is base_path/Company/Project/* so up two levels is universal
			var accountPath = Path.Combine(Application.persistentDataPath, accountFile);
			if (!File.Exists(accountPath)) {
				Debug.Log($"AWSCloudTasks missing account file {accountPath}");
				return;
			}
			var accountData = File.ReadAllText(accountPath);
			var accountJson = JsonConvert.DeserializeObject(accountData) as JObject;
			account = accountJson.ToObject<Account>();
			if (account is null) {
				Debug.Log($"AWSCloudTasks unable to read account file {accountPath}");
			}
		}
		
		// FIXME: Authentication tokens are valid for a limited period of time
		// so reauthentication will be required.

		// User Pool authentication result on success
		// NOTE: member names match keys in AWS JSON response
		[Serializable]
		private class Authentication {
			public string AccessToken;
			public string IdToken;
			public string RefreshToken;
			public string TokenType;
			public string ExpiresIn;
		}

		private Authentication authentication = null;

		private void GetAuthentication() {
			// https://docs.aws.amazon.com/cognito-user-identity-pools/latest/APIReference/API_InitiateAuth.html
			var authorizationRegion = account.userpool.Substring(0, account.userpool.IndexOf('_'));
			var request = new HttpRequestMessage {
				Method = HttpMethod.Post,
				RequestUri = new Uri($"https://cognito-idp.{authorizationRegion}.amazonaws.com"),
				Headers = { { "X-Amz-Target", "AWSCognitoIdentityProviderService.InitiateAuth"} },
				Content = new StringContent(
					JsonConvert.SerializeObject(new {
						ClientId = account.clientId,
						AuthFlow = "USER_PASSWORD_AUTH",
						AuthParameters = new {
							USERNAME = account.username,
							PASSWORD = account.password
						}
					}),
					Encoding.UTF8,
					"application/x-amz-json-1.1" // Content-Type header
					)
			};
			//Debug.Log(request.ToString());
			var response = httpClient.SendAsync(request).Result as HttpResponseMessage;
			//Debug.Log(response.ToString());
			var responseString = response.Content.ReadAsStringAsync().Result;
			var responseObject = JsonConvert.DeserializeObject(responseString) as JObject;
			
			var authenticationJson = responseObject["AuthenticationResult"] as JObject;
			authentication = authenticationJson.ToObject<Authentication>();
			if(authentication == null) {
				Debug.LogWarning($"WSCloudTasks unable to parse response {responseString}");
			}
		}

		// Identity Pool credentials result on success
		// NOTE: member names match keys in AWS JSON response
		[Serializable]
		private class Credentials {
			public string AccessKeyId;
			public string SecretKey;
			public string SessionToken;
			public string Expiration;
		}

		private Credentials credentials = null;

		private void GetCredentials() {
			if(account.identity == null) {
				var authorizationRegion = account.userpool.Substring(0, account.userpool.IndexOf('_'));
				var loginsJson = new JObject();
				loginsJson[$"cognito-idp.{authorizationRegion}.amazonaws.com/{account.userpool}"] = authentication.IdToken;
			
				var contentJson = new JObject();
				contentJson["IdentityPoolId"] = account.policyId;
				contentJson["Logins"] = loginsJson;

				// https://docs.aws.amazon.com/cognitoidentity/latest/APIReference/API_GetId.html
				var credentialsRegion = account.policyId.Substring(0, account.policyId.IndexOf(':'));
				var request = new HttpRequestMessage {
					Method = HttpMethod.Post,
					RequestUri = new Uri($"https://cognito-identity.{credentialsRegion}.amazonaws.com"),
					Headers = {
						{ "X-Amz-Target", "AWSCognitoIdentityService.GetId" }
					},
					Content = new StringContent(
						contentJson.ToString(),
						Encoding.UTF8,
						"application/x-amz-json-1.1" // Content-Type header
					)
				};
				Debug.Log(request.ToString());
				var response = httpClient.SendAsync(request).Result as HttpResponseMessage;
				Debug.Log(response.ToString());
				var responseString = response.Content.ReadAsStringAsync().Result;
				var responseObject = JsonConvert.DeserializeObject(responseString) as JObject;
				
				account.identity = responseObject["IdentityId"].ToString();
				Debug.Log($"AWSCloudTasks identity = {account.identity}");
				// TODO: Identity will not change - this could be recorded
			}

			{
				// FIXME: Regional URLs can be properties of Account instead of repeated here
				var authorizationRegion = account.userpool.Substring(0, account.userpool.IndexOf('_'));
				var loginsJson = new JObject();
				loginsJson[$"cognito-idp.{authorizationRegion}.amazonaws.com/{account.userpool}"] = authentication.IdToken;
				
				var contentJson = new JObject();
				contentJson["IdentityId"] = account.identity;
				contentJson["Logins"] = loginsJson;
				
				// https://docs.aws.amazon.com/cognitoidentity/latest/APIReference/API_GetCredentialsForIdentity.html
				var credentialsRegion = account.policyId.Substring(0, account.policyId.IndexOf(':'));
				var request = new HttpRequestMessage {
					Method = HttpMethod.Post,
					RequestUri = new Uri($"https://cognito-identity.{credentialsRegion}.amazonaws.com"),
					Headers = {
						{ "X-Amz-Target", "AWSCognitoIdentityService.GetCredentialsForIdentity" }
					},
					Content = new StringContent(
						contentJson.ToString(),
						Encoding.UTF8,
						"application/x-amz-json-1.1" // Content-Type header
					)
				};
				Debug.Log(request.ToString());
				var response = httpClient.SendAsync(request).Result as HttpResponseMessage;
				Debug.Log(response.ToString());
				var responseString = response.Content.ReadAsStringAsync().Result;
				var responseObject = JsonConvert.DeserializeObject(responseString) as JObject;
				
				var credentialsJson = responseObject["Credentials"] as JObject;
				credentials = credentialsJson.ToObject<Credentials>();
			}
		}

		public bool authenticated {
			get => authentication != null;
		}

		public AWSCloudTasks() {
			// Authentication flow: https://docs.aws.amazon.com/cognito/latest/developerguide/authentication-flow.html
			LoadAccount();
			if(account == null) return;
			GetAuthentication();
			if(authentication == null) return;
			GetCredentials();
			if(credentials == null) return;
			Debug.Log("AWSCloudTasks authentication SUCCESS");

			// TODO: Load bake and build queues
			// Schedule periodic checking of queues
			// IDEA: Queues should be files in caches
			// or even retrieved from the cloud service
			// NOTE: Baked scene package only needs to include scene
			// lightmaps and any other generated assets.
			// Meshes, materials and textures can be elided.

			var taskList = TaskList();
		}
		
		// https://docs.aws.amazon.com/general/latest/gr/sigv4_signing.html
		// https://docs.microsoft.com/en-us/azure/communication-services/tutorials/hmac-header-tutorial
		// PROBLEM: HttpRequestMessage provides no conversion to canonical form
		// https://github.com/tsibelman/aws-signer-v4-dot-net
		// https://www.jordanbrown.dev/2021/02/06/2021/http-to-raw-string-csharp/
				
		// S3 Specific authentication instructions
		// https://docs.aws.amazon.com/AmazonS3/latest/API/sig-v4-authenticating-requests.html
		
		// NOTE: Current tasks are tracked on cloud.
		// The upload and download folders act as queues.
		// IMPORTANT: Failure logs need to be discoverable by user.

		// TODO: This should be async
		public List<string> TaskList() {
			// List bucket contents
			// NOTE: Policy restricts user to their own folder within a bucket
			var taskList = new List<string>();

			var domainSplit = account.cloudUrl.Split('.');
			var bucket = domainSplit[0];
			var service = domainSplit[1];
			var region = domainSplit[2];

			string continuationToken = "";
			while(continuationToken != null) {
				// https://docs.aws.amazon.com/AmazonS3/latest/API/API_ListObjectsV2.html
				var query = new StringBuilder("?list-type=2");
				// IMPORTANT: If role policy restricts s3:ListObjects to a user identity directory
				// the terminating / must be included in the prefix
				query.Append($"&prefix={account.identity}/");
				var requestUri = new UriBuilder("https", account.cloudUrl);
				if((continuationToken?.Length ?? 0) > 0) {
					query.Append($"&continuation-token={continuationToken}");
				}
				continuationToken = null;
				requestUri.Query = query.ToString();

				// IMPORTANT: Temporary credentials require a session token
				// https://docs.aws.amazon.com/general/latest/gr/sigv4-create-canonical-request.html
				var request = new HttpRequestMessage {
					Method = HttpMethod.Get,
					RequestUri = requestUri.Uri,
					Headers = {
						{ "X-Amz-Security-Token", credentials.SessionToken }
					}
				};
				var signer = new AWS4RequestSigner(credentials.AccessKeyId, credentials.SecretKey);
				request = signer.Sign(request, service, region).Result;

				Debug.Log(request.ToString());
				var response = httpClient.SendAsync(request).Result as HttpResponseMessage;
				Debug.Log(response.ToString());
				var responseString = response.Content.ReadAsStringAsync().Result;

				// Response is only in XML
				// https://docs.aws.amazon.com/AmazonS3/latest/API/API_ListObjectsV2.html
				// https://docs.microsoft.com/en-us/dotnet/api/system.xml.xmldocument
				var xmlReader = new XmlDocument();
				xmlReader.LoadXml(responseString);
				var root = xmlReader.DocumentElement;
				if(root.HasChildNodes) {
					for(int i = 0; i < root.ChildNodes.Count; i++) {
						var node = root.ChildNodes[i];
						Debug.Log(node.OuterXml);
						if(node.Name == "Contents") {
							Debug.Log($"ObjectKey: {node["Key"].InnerText}");
							if(node["Size"].InnerText != "0") {
								// Directory objects have size equal to zero
								taskList.Add(node["Key"].InnerText);
							}
						}
						if(node.Name == "NextContinuationToken") {
							// NextContinuationToken is only included if files remain
							continuationToken = node.InnerText;
						}
					}
				}
			}

			return taskList;
		}

		// TODO: This should be async
		// Upload model package for baking
		public void UploadBake() {
			// S3 object upload (multi-part?)
			// Enqueue periodic checking for result
		}

		// TODO: This should be async
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
			// NOTE: In the case of timed access it should still be possible
			// to download the model before but NOT after the access window.

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
	}
}