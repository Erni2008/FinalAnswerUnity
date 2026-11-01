using UnityEditor;

namespace FinalAnswer.EditorTools
{
    /// <summary>
    /// Import settings for migrated shell assets. Textures keep their exact source size (no NPOT rescale,
    /// no max-size downscale) because the measured Java crop rectangles are in original pixels.
    /// </summary>
    public sealed class ImportRules : AssetPostprocessor
    {
        const string Root = "Assets/FinalAnswerShell/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root + "Art/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            importer.filterMode = UnityEngine.FilterMode.Bilinear;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.sRGBTexture = true;
        }

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Root + "Audio/")) return;
            var importer = (AudioImporter)assetImporter;
            var settings = importer.defaultSampleSettings;
            var longClip = assetPath.Contains("/Environment/");
            settings.loadType = longClip ? UnityEngine.AudioClipLoadType.Streaming : UnityEngine.AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = UnityEngine.AudioCompressionFormat.Vorbis;
            settings.quality = .7f;
            importer.defaultSampleSettings = settings;
            importer.loadInBackground = longClip;
        }
    }
}
