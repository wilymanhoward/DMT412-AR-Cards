using UnityEditor;

// Pictures in Assets/Resources/Portfolio are shown exactly as they are: Unity's default would stretch them to a
// power-of-two size (a 960x540 screenshot would come out squashed) and add mipmaps they don't need.
public class PortfolioImportSettings : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.Contains("/Resources/Portfolio/")) return;
        var importer = (TextureImporter)assetImporter;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.mipmapEnabled = false;
        importer.maxTextureSize = 2048;
    }
}
