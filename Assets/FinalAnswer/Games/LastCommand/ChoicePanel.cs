using UnityEngine;
using TMPro;
namespace FinalAnswer {
 public sealed class ChoicePanel:InteractiveMechanism {
  public RectTransform Surface;public string[] Values;public string Selected{get;private set;}="";bool initialized;
  public void Initialize(TMP_FontAsset font){if(initialized)return;initialized=true;UIFactory.Text(Surface,font,Id=="ANIMAL_PANEL"?"ВЫБОР ЖИВОТНОГО":"ВЫБОР СИМВОЛА",10,8,780,50,28);for(int i=0;i<Values.Length;i++){string v=Values[i];UIFactory.Button(Surface,font,v,10+i*195,75,182,100,()=>Select(v));}}
  public void Select(string value){if(!Available)return;Selected=value;Send("SELECT",value);}
  public void SetSelection(string value)=>Selected=value;
 }
}
