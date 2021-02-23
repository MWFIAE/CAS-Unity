﻿#if UNITY_IOS || CASDeveloper
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace CAS.UEditor
{
    internal class CASPostprocessBuild
    {
        private const string casTitle = "CAS Postprocess Build";

        [PostProcessBuild]
        public static void OnPostProcessBuild( BuildTarget buildTarget, string path )
        {
            if (buildTarget != BuildTarget.iOS)
                return;

            try
            {
                var casSettings = CASEditorUtils.GetSettingsAsset( BuildTarget.iOS );

                string plistPath = Path.Combine( path, "Info.plist" );
                ConfigureInfoPlist( plistPath, casSettings );

                var projectPath = Path.Combine( path, "Unity-iPhone.xcodeproj/project.pbxproj" );
                var project = ConfigureXCodeProject( path, projectPath, casSettings );
                ApplyCrosspromoDynamicLinks( projectPath, project, casSettings );
                Debug.Log( CASEditorUtils.logTag + "Postrocess Build done." );
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

#if UNITY_2019_3_OR_NEWER
        [PostProcessBuild( 47 )]//must be between 40 and 50 to ensure that it's not overriden by Podfile generation (40) and that it's added before "pod install" (50)
        private static void FixPodFileBug( BuildTarget target, string buildPath )
        {
            if (target != BuildTarget.iOS)
                return;
            var podPath = buildPath + "/Podfile";
            if (!File.Exists( podPath ))
            {
                Debug.LogError( CASEditorUtils.logTag + "Podfile not found.\n" +
                    "Please add `target 'Unity-iPhone' do end` to the Podfile in root folder of XCode project and call `pod install --no-repo-update`" );
                return;
            }
            var content = File.ReadAllText( podPath );
            if (content.Contains( "'Unity-iPhone'" ))
                return;
#if false //Develop
            var depends = new List<string>();
            var isFramework = false;

            using (StreamWriter sw = new StreamWriter( podPath, false ))
            {
                for (int i = 0; i < content.Length; i++)
                {
                    var line = content[i];
                    if (line.Contains( "'Unity-iPhone'" ))
                        return;
                    if (!isFramework && line.Contains( "use_frameworks!" ))
                        isFramework = true;
                    if (line.Contains( "CleverAdsSolutions-" ))
                        depends.Add( line );
                }
            }
#endif
            using (StreamWriter sw = File.AppendText( podPath ))
            {
                sw.WriteLine();
                sw.WriteLine( "target 'Unity-iPhone' do" );
                sw.WriteLine( "end" );
            }
        }
#endif

        private static void ConfigureInfoPlist( string plistPath, CASInitSettings casSettings )
        {
            EditorUtility.DisplayProgressBar( casTitle, "Read iOS Info.plist", 0.3f );
            PlistDocument plist = new PlistDocument();
            plist.ReadFromFile( plistPath );

            #region Read Admob App ID from CAS Settings
            EditorUtility.DisplayProgressBar( casTitle, "Write Admob App ID to Info.plist", 0.35f );


            string admobAppId = null;
            if (casSettings.testAdMode)
            {
                admobAppId = CASEditorUtils.iosAdmobSampleAppID;
            }
            else if (casSettings.managersCount > 0)
            {
                string settingsPath = CASEditorUtils.GetNativeSettingsPath( BuildTarget.iOS, casSettings.GetManagerId( 0 ) );
                if (File.Exists( settingsPath ))
                {
                    try
                    {
                        admobAppId = CASEditorUtils.GetAdmobAppIdFromJson( File.ReadAllText( settingsPath ) );
                    }
                    catch (Exception e)
                    {
                        CASEditorUtils.StopBuildWithMessage( e.ToString(), BuildTarget.iOS );
                    }
                }
            }

            #endregion

            if (admobAppId != null)
                plist.root.SetString( "GADApplicationIdentifier", admobAppId );
            plist.root.SetBoolean( "GADDelayAppMeasurementInit", true );

            #region Write NSUserTrackingUsageDescription
            EditorUtility.DisplayProgressBar( casTitle, "Write NSUserTrackingUsageDescription to Info.plist", 0.5f );
            if (casSettings && !string.IsNullOrEmpty( casSettings.defaultIOSTrakingUsageDescription ))
                plist.root.SetString( "NSUserTrackingUsageDescription", casSettings.defaultIOSTrakingUsageDescription );
            #endregion

            #region Write SKAdNetworks
            EditorUtility.DisplayProgressBar( casTitle, "Write SKAdNetworks to Info.plist", 0.5f );
            var templateFile = CASEditorUtils.GetTemplatePath( CASEditorUtils.iosSKAdNetworksTemplateFile );
            if (string.IsNullOrEmpty( templateFile ))
            {
                Debug.LogError( CASEditorUtils.logTag + "Not found SKAdNetworkItems. Try reimport CAS package." );
            }
            else
            {
                var networksLines = File.ReadAllLines( templateFile );

                PlistElementArray adNetworkItems;
                var adNetworkItemsField = plist.root["SKAdNetworkItems"];
                if (adNetworkItemsField == null)
                    adNetworkItems = plist.root.CreateArray( "SKAdNetworkItems" );
                else
                    adNetworkItems = adNetworkItemsField.AsArray();

                for (int i = 0; i < networksLines.Length; i++)
                {
                    if (!string.IsNullOrEmpty( networksLines[i] ))
                    {
                        var dict = adNetworkItems.AddDict();
                        dict.SetString( "SKAdNetworkIdentifier", networksLines[i] );
                    }
                }
            }
            #endregion

            #region Write LSApplicationQueriesSchemes
            EditorUtility.DisplayProgressBar( casTitle, "Write LSApplicationQueriesSchemes to Info.plist", 0.6f );
            PlistElementArray applicationQueriesSchemes;
            var applicationQueriesSchemesField = plist.root["LSApplicationQueriesSchemes"];
            if (applicationQueriesSchemesField == null)
                applicationQueriesSchemes = plist.root.CreateArray( "LSApplicationQueriesSchemes" );
            else
                applicationQueriesSchemes = applicationQueriesSchemesField.AsArray();
            foreach (var scheme in new[] { "fb", "instagram", "tumblr", "twitter" })
                if (applicationQueriesSchemes.values.Find( x => x.AsString() == scheme ) == null)
                    applicationQueriesSchemes.AddString( scheme );
            #endregion

            EditorUtility.DisplayProgressBar( casTitle, "Save Info.plist to XCode project", 0.65f );
            File.WriteAllText( plistPath, plist.WriteToString() );
        }

        private static PBXProject ConfigureXCodeProject( string rootPath, string projectPath, CASInitSettings casSettings )
        {
            EditorUtility.DisplayProgressBar( casTitle, "Configure XCode project", 0.7f );
            var project = new PBXProject();
            project.ReadFromString( File.ReadAllText( projectPath ) );
#if UNITY_2019_3_OR_NEWER
            var target = project.GetUnityMainTargetGuid();
#else
            var target = project.TargetGuidByName( PBXProject.GetUnityTargetName() );
#endif
            project.SetBuildProperty( target, "ENABLE_BITCODE", "No" );
            project.AddBuildProperty( target, "OTHER_LDFLAGS", "-ObjC" );
            //project.AddBuildProperty( target, "OTHER_LDFLAGS", "-lxml2 -ObjC -fobjc-arc" );
            //project.AddBuildProperty( target, "CLANG_ENABLE_MODULES", "YES" ); // InMobi required
            project.SetBuildProperty( target, "SWIFT_VERSION", "5.0" );

            EditorUtility.DisplayProgressBar( casTitle, "Copy CAS Settings json to project", 0.8f );

            var resourcesBuildPhase = project.GetResourcesBuildPhaseByTarget( target );
            for (int i = 0; i < casSettings.managersCount; i++)
            {
                int managerIdLength = casSettings.GetManagerId( i ).Length;
                string suffix = managerIdLength + casSettings.GetManagerId( i )[managerIdLength - 1] + ".json";
                string fileName = "cas_settings" + suffix;
                string pathInAssets = CASEditorUtils.iosResSettingsPath + suffix;
                if (File.Exists( pathInAssets ))
                {
                    try
                    {
                        File.Copy( pathInAssets, Path.Combine( rootPath, fileName ) );
                        var fileGuid = project.AddFile( fileName, fileName, PBXSourceTree.Source );
                        project.AddFileToBuildSection( target, resourcesBuildPhase, fileGuid );
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning( CASEditorUtils.logTag + "Copy Raw Files To XCode Project failed with error: " + e.ToString() );
                    }
                }
            }

            EditorUtility.DisplayProgressBar( casTitle, "Save XCode project", 0.9f );
            File.WriteAllText( projectPath, project.WriteToString() );
            return project;
        }

        private static void ApplyCrosspromoDynamicLinks( string projectPath, PBXProject project, CASInitSettings casSettings )
        {
            if (casSettings.managersCount == 0 || string.IsNullOrEmpty( casSettings.GetManagerId( 0 ) ))
                return;
            try
            {
                EditorUtility.DisplayProgressBar( casTitle, "Apply Crosspromo Dynamic Links", 0.9f );
                var identifier = Application.identifier;
                var productName = identifier.Substring( identifier.LastIndexOf( "." ) + 1 );

                var entitlements = new ProjectCapabilityManager( projectPath, productName + ".entitlements",
#if UNITY_2019_3_OR_NEWER
                    project.GetUnityMainTargetGuid() );
#else
                    PBXProject.GetUnityTargetName() );
#endif
                string link = "applinks:psvios" + casSettings.GetManagerId( 0 ) + ".page.link";
                entitlements.AddAssociatedDomains( new[] { link } );
                entitlements.WriteToFile();
                Debug.Log( CASEditorUtils.logTag + "Apply application shame: " + link );
            }
            catch (Exception e)
            {
                Debug.LogError( CASEditorUtils.logTag + "Dynamic link creation fail: " + e.ToString() );
            }
        }
    }
}
#endif