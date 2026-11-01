using UnityEngine;
namespace FinalAnswer {
 public sealed class IndicatorLight:InteractiveMechanism {
  public Light Glow;public Renderer Lens;public string ColorId{get;private set;}="OFF";MaterialPropertyBlock properties;readonly string[] cycle={"OFF","RED","GREEN","YELLOW","BLUE"};
  protected override void Awake(){base.Awake();properties=new MaterialPropertyBlock();}
  public static Color ColorFor(string id){switch(id){case "RED":return new Color(1,.08f,.025f);case "GREEN":return new Color(.12f,1,.3f);case "BLUE":return new Color(.08f,.35f,1);case "YELLOW":return new Color(1,.62f,.07f);default:return new Color(.04f,.06f,.05f);}}
  public void SetColor(string id){ColorId=id;Color c=ColorFor(id);if(Glow){Glow.color=c;Glow.intensity=id=="OFF"?0:.4f;}if(Lens){Lens.GetPropertyBlock(properties);properties.SetColor("_EmissionColor",c*(id=="OFF"?0:2.5f));properties.SetColor("emissiveFactor",c*(id=="OFF"?0:1.5f));Lens.SetPropertyBlock(properties);}}
  public override void Interact(float direction=1){if(!Available)return;SetColor(cycle[(System.Array.IndexOf(cycle,ColorId)+1)%cycle.Length]);Send("SET",ColorId);}
 }
}
