using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
namespace FinalAnswer {
 public static class FrameCapture {
  public static void Write(Camera camera,string path){var rt=new RenderTexture(1600,1000,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);rt.Create();var previous=RenderTexture.active;float aspect=camera.aspect;try{camera.aspect=1.6f;var request=new RenderPipeline.StandardRequest{destination=rt};RenderPipeline.SubmitRenderRequest(camera,request);RenderTexture.active=rt;var texture=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);texture.Apply();Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,texture.EncodeToPNG());Object.Destroy(texture);}finally{camera.aspect=aspect;RenderTexture.active=previous;rt.Release();Object.Destroy(rt);}}
 }
}
