﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamSharp.Test.TestHelpers;
using System;
using System.Diagnostics;
using System.Net;
using System.Web;

namespace SteamSharp.Test {
	
	[TestClass]
	public class Authenticators {

		[TestMethod]
		[TestCategory( "Authenticators" )]
		public void Verify_APIKey_Added() {

			// Did you remember to add your API Token?
			Assert.IsNotNull( ResourceConstants.APIKey );
			Assert.AreNotEqual( ResourceConstants.APIKey, "" );

			using( SimulatedServer.Create( ResourceConstants.SimulatedServerUrl, ApiKeyEchoHandler ) ) {

				var client = new SteamClient( ResourceConstants.SimulatedServerUrl );

				client.Authenticator = SteamSharp.Authenticators.APIKeyAuthenticator.ForProtectedResource( ResourceConstants.APIKey );

				SteamRequest request = new SteamRequest( "/resource" );
				var response = client.Execute( request );

				Assert.AreEqual( "key|" + ResourceConstants.APIKey, response.Content );

			}

		}

		private static void ApiKeyEchoHandler( HttpListenerContext context ) {

			var data = context.Request;

			Uri requestUri = context.Request.Url;
			var queryParams = HttpUtility.ParseQueryString( requestUri.Query );

			string output = "";
			if( queryParams["key"] != null )
				output = "key|" + queryParams["key"];

			context.Response.OutputStream.WriteStringUTF8( output );

		}

		private static void RSAStringEchoHandler( HttpListenerContext context ) {

			var data = context.Request;

			Uri requestUri = context.Request.Url;
			var queryParams = HttpUtility.ParseQueryString( requestUri.Query );

			string output = "";
			if( queryParams["key"] != null )
				output = "key|" + queryParams["key"];

			context.Response.OutputStream.WriteStringUTF8( output );

		}

	}

}
