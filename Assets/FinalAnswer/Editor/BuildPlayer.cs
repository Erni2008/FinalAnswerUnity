using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
namespace FinalAnswer.Editor {
 public static class BuildPlayer {
  public static void PrepareAndBuild(){ProjectBuilder.Build();Mac();}
  [MenuItem("Final Answer/Build Windows x64")]
  public static void Windows(){if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone,BuildTarget.StandaloneWindows64))throw new InvalidOperationException("Install Windows Build Support for Unity 6000.6.7f1 in Unity Hub first.");Directory.CreateDirectory("Builds/Windows");var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/FinalAnswer/Scenes/Bootstrap.unity","Assets/FinalAnswer/Scenes/TheLastCommand.unity","Assets/FinalAnswer/Scenes/GameHall.unity"},locationPathName="Builds/Windows/FinalAnswerUnity.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Windows build failed");}
  [MenuItem("Final Answer/Build macOS Apple Silicon")]
  public static void Mac(){Directory.CreateDirectory("Builds/macOS");PlayerSettings.SetArchitecture(UnityEditor.Build.NamedBuildTarget.Standalone,1);var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/FinalAnswer/Scenes/Bootstrap.unity","Assets/FinalAnswer/Scenes/TheLastCommand.unity","Assets/FinalAnswer/Scenes/GameHall.unity"},locationPathName="Builds/macOS/FinalAnswerUnity.app",target=BuildTarget.StandaloneOSX,options=BuildOptions.Development});if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Player build: "+report.summary.result);Debug.Log("MACOS_ARM64_BUILD_OK: "+report.summary.totalSize);}
 }
}
