using System;
using UnityEngine;
namespace FinalAnswer {
 [CreateAssetMenu(menuName="Final Answer/Audio Catalog")]
 public sealed class AudioCatalog:ScriptableObject {
  [Serializable]public sealed class Cue {public string id;public AudioClip clip;public float gain=1;public int priority=2,throttleMs;}
  public Cue[] cues=Array.Empty<Cue>();
 }
}
