using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace FinalAnswer {
 public static class UIFactory {
  public static readonly Color Amber=new Color(.95f,.64f,.25f),Ink=new Color(.025f,.042f,.042f,.96f);
  public static RectTransform Rect(string name,Transform parent,float x,float y,float w,float h){var g=new GameObject(name,typeof(RectTransform));var r=(RectTransform)g.transform;r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r;}
  public static Image Panel(string name,Transform parent,float x,float y,float w,float h,Color color){var r=Rect(name,parent,x,y,w,h);var i=r.gameObject.AddComponent<Image>();i.color=color;return i;}
  public static TextMeshProUGUI Text(Transform parent,TMP_FontAsset font,string value,float x,float y,float w,float h,int size=22,Color? color=null){var r=Rect("Text",parent,x,y,w,h);var t=r.gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.text=value;t.fontSize=size;t.color=color??Amber;t.raycastTarget=false;t.textWrappingMode=TextWrappingModes.Normal;t.overflowMode=TextOverflowModes.Ellipsis;t.richText=false;return t;}
  public static Button Button(Transform parent,TMP_FontAsset font,string title,float x,float y,float w,float h,Action click,Color? color=null){var image=Panel(title,parent,x,y,w,h,color??new Color(.10f,.14f,.14f));var b=image.gameObject.AddComponent<Button>();var c=b.colors;c.highlightedColor=new Color(1.3f,1.3f,1.3f);c.pressedColor=new Color(.7f,.7f,.7f);b.colors=c;b.onClick.AddListener(()=>{click?.Invoke();if(UnityEngine.EventSystems.EventSystem.current)UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);});var t=Text(b.transform,font,title,8,3,w-16,h-6,22);t.alignment=TextAlignmentOptions.Center;t.enableAutoSizing=true;t.fontSizeMin=14;t.fontSizeMax=22;return b;}
  public static TMP_InputField Input(Transform parent,TMP_FontAsset font,string initial,float x,float y,float w,float h){var box=Panel("Input",parent,x,y,w,h,new Color(.06f,.09f,.09f));var input=box.gameObject.AddComponent<TMP_InputField>();var txt=Text(box.transform,font,initial,10,5,w-20,h-10,20);txt.overflowMode=TextOverflowModes.Overflow;input.textViewport=txt.rectTransform;input.textComponent=txt;input.text=initial;return input;}
 }
}
