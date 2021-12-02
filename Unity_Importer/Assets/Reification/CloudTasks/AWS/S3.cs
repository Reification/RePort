using System;
using System.IO;
using System.Net.Http;
using System.Collections.Generic;
using System.Text;
using System.Xml;

// Log errors in Unity console
// TODO: Instead, throw with error log as message
using UnityEngine;

namespace Reification.CloudTasks.AWS {
	public class S3 {
		private static readonly HttpClient httpClient = new HttpClient();
		
		public const string cacheFolder = "CloudTasks";

		private Cognito authorization;

		public S3(Cognito authorization) {
			this.authorization = authorization;
		}
		
		// QUESTION: Is there a way to make the operations asynchronous with a callback?
		
		// https://docs.aws.amazon.com/general/latest/gr/sigv4_signing.html
		// https://docs.microsoft.com/en-us/azure/communication-services/tutorials/hmac-header-tutorial
		// PROBLEM: HttpRequestMessage provides no conversion to canonical (sent) form
		// However, the AWS canonical form is more restrictive than the accepted message form
		// https://github.com/tsibelman/aws-signer-v4-dot-net
		// https://www.jordanbrown.dev/2021/02/06/2021/http-to-raw-string-csharp/

		// S3 Specific authentication instructions
		// https://docs.aws.amazon.com/AmazonS3/latest/API/sig-v4-authenticating-requests.html
		
		// IMPORTANT: If listing all client files, path must end in /
		
		public bool ListFiles(string cloudPath, out List<string> taskList) {
			taskList = new List<string>();
			if(!authorization.authenticated) return false;

			var domainSplit = authorization.cloudUrl.Split('.');
			var bucket = domainSplit[0];
			var service = domainSplit[1];
			var region = domainSplit[2];

			string continuationToken = "";
			while(continuationToken != null) {
				// https://docs.aws.amazon.com/AmazonS3/latest/API/API_ListObjectsV2.html
				var query = new StringBuilder("?list-type=2");
				// IMPORTANT: If role policy restricts s3:ListObjects to a user identity directory
				// the terminating / must be included in the prefix
				query.Append($"&prefix={cloudPath}");
				var requestUri = new UriBuilder("https", authorization.cloudUrl);
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
						{ "X-Amz-Security-Token", authorization.credentials.SessionToken }
					}
				};

				var signer = new AWS4RequestSigner(authorization.credentials.AccessKeyId, authorization.credentials.SecretKey);
				request = signer.Sign(request, service, region).Result;
				var response = httpClient.SendAsync(request).Result;
				if(!response.IsSuccessStatusCode) {
					Debug.LogWarning($"Request failed with status code = {response.StatusCode}");
					return false;
				}
				var responseString = response.Content.ReadAsStringAsync().Result;

				// Response is XML
				// https://docs.aws.amazon.com/AmazonS3/latest/API/API_ListObjectsV2.html
				// https://docs.microsoft.com/en-us/dotnet/api/system.xml.xmldocument
				var xmlReader = new XmlDocument();
				xmlReader.LoadXml(responseString);
				var root = xmlReader.DocumentElement;
				if(root.HasChildNodes) {
					for(int i = 0; i < root.ChildNodes.Count; i++) {
						var node = root.ChildNodes[i];
						if(node.Name == "Contents") {
							if(node["Size"].InnerText != "0") {
								// Directory objects have size equal to zero
								taskList.Add(node["Key"].InnerText);
								// TODO: Also retain the ETag data. In the case of SSE-S3 or unencrypted data this is the MD5 hash
								// https://docs.aws.amazon.com/AmazonS3/latest/API/API_Object.html#AmazonS3-Type-Object-ETag
							}
						}
						if(node.Name == "NextContinuationToken") {
							// NextContinuationToken is only included if files remain
							continuationToken = node.InnerText;
						}
					}
				}
			}

			return true;
		}
		
		// Upload model package for baking
		// NOTE: Multi-part upload could be used for large files
		// https://docs.aws.amazon.com/AmazonS3/latest/userguide/mpuoverview.html
		// NOTE: In the case of a slow connection this could help with credential timeout
		public bool PutFile(string localPath, string cloudPath) {
			if(!File.Exists(localPath)) return false;
			if(!authorization.authenticated) return false;

			// OPTIMIZATION: Before uploading look for existence of file and compare MD5 hash
			// In the case of a baked package the file may exist, so upload can be skipped.

			var domainSplit = authorization.cloudUrl.Split('.');
			var bucket = domainSplit[0];
			var service = domainSplit[1];
			var region = domainSplit[2];

			// https://docs.aws.amazon.com/AmazonS3/latest/API/API_GetObject.html
			var requestUri = new UriBuilder("https", authorization.cloudUrl);
			requestUri.Path = cloudPath;

			// IMPORTANT: Temporary credentials require a session token
			// https://docs.aws.amazon.com/general/latest/gr/sigv4-create-canonical-request.html
			var request = new HttpRequestMessage {
				Method = HttpMethod.Put,
				RequestUri = requestUri.Uri,
				Headers = {
					{ "X-Amz-Security-Token", authorization.credentials.SessionToken }
				}
			};

			var localFile = File.ReadAllBytes(localPath);
			request.Content = new ByteArrayContent(localFile);
			// TODO: Compute and compare MD5 hash to verify transmission

			var signer = new AWS4RequestSigner(authorization.credentials.AccessKeyId, authorization.credentials.SecretKey);
			request = signer.Sign(request, service, region).Result;
			var response = httpClient.SendAsync(request).Result;
			if(!response.IsSuccessStatusCode) {
				Debug.LogWarning($"Request failed with status code = {response.StatusCode}");
				return false;
			}

			return true;
		}
		
		// NOTE: Piecewise downloading via range argument is possible
		public bool GetFile(string cloudPath, string localPath) {
			if(!authorization.authenticated) return false;
			
			var domainSplit = authorization.cloudUrl.Split('.');
			var bucket = domainSplit[0];
			var service = domainSplit[1];
			var region = domainSplit[2];

			// https://docs.aws.amazon.com/AmazonS3/latest/API/API_GetObject.html
			var requestUri = new UriBuilder("https", authorization.cloudUrl);
			requestUri.Path = cloudPath;

			// IMPORTANT: Temporary credentials require a session token
			// https://docs.aws.amazon.com/general/latest/gr/sigv4-create-canonical-request.html
			var request = new HttpRequestMessage {
				Method = HttpMethod.Get,
				RequestUri = requestUri.Uri,
				Headers = {
					{ "X-Amz-Security-Token", authorization.credentials.SessionToken }
				}
			};

			var signer = new AWS4RequestSigner(authorization.credentials.AccessKeyId, authorization.credentials.SecretKey);
			request = signer.Sign(request, service, region).Result;
			var response = httpClient.SendAsync(request).Result;
			if(!response.IsSuccessStatusCode) {
				Debug.LogWarning($"Request failed with status code = {response.StatusCode}");
				return false;
			}

			Directory.CreateDirectory(Path.GetDirectoryName(localPath));
			var localFile = File.Create(localPath);
			var remoteFile = response.Content.ReadAsStreamAsync().Result;
			remoteFile.CopyTo(localFile);
			// TODO: Compute and compare MD5 hash to verify transmission

			return true;
		}
		
		// NOTE: A single POST request can be used to delete multiple objects
		// https://docs.aws.amazon.com/AmazonS3/latest/API/API_DeleteObjects.html
		public bool DeleteFile(string cloudPath) {
			if(!authorization.authenticated) return false;
			
			var domainSplit = authorization.cloudUrl.Split('.');
			var bucket = domainSplit[0];
			var service = domainSplit[1];
			var region = domainSplit[2];

			// https://docs.aws.amazon.com/AmazonS3/latest/API/API_GetObject.html
			var requestUri = new UriBuilder("https", authorization.cloudUrl);
			requestUri.Path = cloudPath;

			// IMPORTANT: Temporary credentials require a session token
			// https://docs.aws.amazon.com/general/latest/gr/sigv4-create-canonical-request.html
			var request = new HttpRequestMessage {
				Method = HttpMethod.Delete,
				RequestUri = requestUri.Uri,
				Headers = {
					{ "X-Amz-Security-Token", authorization.credentials.SessionToken }
				}
			};

			var signer = new AWS4RequestSigner(authorization.credentials.AccessKeyId, authorization.credentials.SecretKey);
			request = signer.Sign(request, service, region).Result;
			var response = httpClient.SendAsync(request).Result;
			if(!response.IsSuccessStatusCode) {
				Debug.LogWarning($"Request failed with status code = {response.StatusCode}");
				return false;
			}

			return true;
		}
	}
}