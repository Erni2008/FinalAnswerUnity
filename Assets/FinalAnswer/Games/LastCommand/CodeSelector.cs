using UnityEngine;
using TMPro;
namespace FinalAnswer {
 public sealed class CodeSelector:InteractiveMechanism {
  public RectTransform Surface;TMP_Text digits;public string Code{get;private set;}="0000";
  public void Initialize(TMP_FontAsset font){if(digits)return;digits=UIFactory.Text(Surface,font,Code,0,55,600,85,52);digits.alignment=TextAlignmentOptions.Center;for(int i=0;i<4;i++){int index=i;UIFactory.Button(Surface,font,"+",i*150,0,142,50,()=>Change(index,1));UIFactory.Button(Surface,font,"−",i*150,145,142,50,()=>Change(index,-1));}}
  public void SetCode(string code){if(code.Length!=4)return;Code=code;if(digits)digits.text=code;}
  public void Change(int index,int delta){if(!Available)return;var c=Code.ToCharArray();c[index]=(char)('0'+(c[index]-'0'+delta+10)%10);SetCode(new string(c));Send("STEP",index+":"+(delta>0?"+1":"-1"));}
  public override void Interact(float direction=1)=>Change(0,direction<0?-1:1);
 }
}
