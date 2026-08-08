using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{

    private sealed class LGuiNpcOriginalCardPreview : MonoBehaviour
    {
        private const int PreviewLayer = 31;

        private Image? _fallback;
        private RawImage? _target;
        private CharaRenderer? _sourceRenderer;
        private CardActor? _sourceActor;
        private RenderParam? _sourceRenderParam;
        private readonly Vector3 _sourceRenderPosition = new(20000f, 20000f, 0f);
        private GameObject? _previewObject;
        private MeshFilter? _previewMeshFilter;
        private MeshRenderer? _previewMeshRenderer;
        private SpriteRenderer? _previewRenderer;
        private GameObject? _cameraObject;
        private Camera? _camera;
        private RenderTexture? _renderTexture;
        private MaterialPropertyBlock? _propertyBlock;
        private Sprite? _lastSprite;
        private bool _usesOriginalTilePass;
        private bool _fallbackHidden;
        private bool _cleaned;

        public bool Initialize(Image fallback, RawImage target, SourceChara.Row row, Sprite sprite, string npcId)
        {
            try
            {
                _fallback = fallback;
                _target = target;
                var placementSprite = sprite;

                Material? material = null;
                Mesh? tileMesh = null;
                var tile = 0f;
                var pass = row.renderData?.pass;
                if (row._tiles != null && row._tiles.Length > 0 && pass != null && pass.mesh != null && pass.mat != null)
                {
                    // This is the first branch used by TraitFigure for normal
                    // NPC rows. Keep its mesh, atlas tile, material and the
                    // special card matColor (-3) intact; converting -3 to an
                    // ether RGB token switches to the static actor fallback.
                    _usesOriginalTilePass = true;
                    tileMesh = pass.mesh;
                    material = pass.mat;
                    tile = row._tiles[0];
                }
                else
                {
                    try
                    {
                        var owner = GameAccess.Spawn.CreateCharacter(npcId, -1);
                        if (owner != null)
                        {
                            // This is the fallback branch used by TraitFigure
                            // for PCC/NPC rows without placement tiles.
                            _sourceRenderer = new CharaRenderer();
                            _sourceRenderer.SetOwner(owner);
                            _sourceRenderParam = new RenderParam(owner.GetRenderParam());
                            var sourceEther = GameAccess.Runtime.Core.Colors.matColors["ether"].main;
                            _sourceRenderParam.matColor = -BaseTileMap.GetColorInt(ref sourceEther, 100);
                            DrawSourceRenderer();
                            _sourceActor = _sourceRenderer.actor;
                            if (_sourceActor != null)
                            {
                                var actorSprite = _sourceActor.sr?.sprite;
                                if (actorSprite != null)
                                {
                                    // Generic/template rows can create a
                                    // random Chara even though their actual
                                    // placement model is only a placeholder.
                                    // Do not present that unrelated actor as
                                    // the NPC's card model.
                                    if (!IsSamePlacementModel(placementSprite, actorSprite))
                                    {
                                        ReleaseSourceRenderer();
                                        return false;
                                    }
                                    sprite = actorSprite;
                                }
                                material = _sourceActor.sr?.sharedMaterial;
                            }
                        }
                    }
                    catch
                    {
                        ReleaseSourceRenderer();
                    }
                }

                material ??= row.renderData?.pass?.mat;
                if (material == null || sprite.texture == null)
                    return false;

                _previewObject = new GameObject("ElinModifierNpcCardPreviewSprite")
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    layer = PreviewLayer
                };
                if (_usesOriginalTilePass && tileMesh != null)
                {
                    _previewMeshFilter = _previewObject.AddComponent<MeshFilter>();
                    _previewMeshFilter.sharedMesh = tileMesh;
                    _previewMeshRenderer = _previewObject.AddComponent<MeshRenderer>();
                    _previewMeshRenderer.sharedMaterial = material;
                }
                else
                {
                    _previewRenderer = _previewObject.AddComponent<SpriteRenderer>();
                    _previewRenderer.sharedMaterial = material;
                    _previewRenderer.sprite = sprite;
                    _previewRenderer.color = Color.white;
                }

                _cameraObject = new GameObject("ElinModifierNpcCardPreviewCamera")
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    layer = PreviewLayer
                };
                _camera = _cameraObject.AddComponent<Camera>();
                _camera.enabled = false;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = Color.clear;
                _camera.orthographic = true;
                _camera.allowHDR = false;
                _camera.allowMSAA = false;
                _camera.useOcclusionCulling = false;
                _camera.cullingMask = 1 << PreviewLayer;

                _renderTexture = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32)
                {
                    name = "ElinModifierNpcCardPreviewTexture",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    antiAliasing = 1
                };
                _renderTexture.Create();
                _camera.targetTexture = _renderTexture;

                _target.texture = _renderTexture;
                _target.material = null;
                _target.uvRect = new Rect(0f, 0f, 1f, 1f);
                _target.color = Color.white;
                _target.raycastTarget = false;

                _propertyBlock = new MaterialPropertyBlock();
                if (_usesOriginalTilePass && _previewMeshRenderer != null)
                {
                    // MeshPass.Draw supplies these as instanced arrays. Using
                    // the same names and values preserves the original card
                    // shader branch, including its time-driven effect.
                    _propertyBlock.SetFloatArray("_Tiles", new[] { tile });
                    _propertyBlock.SetFloatArray("_Color", new[] { 11010048f });
                    _propertyBlock.SetFloatArray("_MatColor", new[] { -3f });
                    _previewMeshRenderer.SetPropertyBlock(_propertyBlock);
                    _lastSprite = sprite;
                    if (_fallback != null)
                        _fallback.sprite = sprite;
                }
                else
                {
                    var ether = GameAccess.Runtime.Core.Colors.matColors["ether"].main;
                    var etherMatColor = -BaseTileMap.GetColorInt(ref ether, 100);
                    if (_sourceActor?.sr != null)
                        _sourceActor.sr.GetPropertyBlock(_propertyBlock);
                    ApplySprite(sprite, etherMatColor);
                }
                RenderPreview();
                // The fallback image occupies the same area below the
                // transparent RenderTexture. Leaving it enabled causes a
                // static blue character to remain visible behind the original
                // card shader animation.
                if (_fallback != null)
                {
                    _fallback.enabled = false;
                    _fallbackHidden = true;
                }
                return true;
            }
            catch
            {
                Cleanup();
                return false;
            }
        }

        private void Update()
        {
            if (_usesOriginalTilePass)
            {
                RenderPreview();
                return;
            }

            if (_sourceActor == null || _previewRenderer == null)
            {
                RenderPreview();
                return;
            }

            try
            {
                // Drive the exact renderer path used by a placed TraitCard.
                // CharaRenderer.UpdatePosition advances the original actor
                // frames using the current game animation settings.
                DrawSourceRenderer();

                var sprite = _sourceActor.sr?.sprite;
                if (sprite != null && sprite != _lastSprite)
                {
                    if (_propertyBlock != null)
                        _sourceActor.sr.GetPropertyBlock(_propertyBlock);
                    var ether = GameAccess.Runtime.Core.Colors.matColors["ether"].main;
                    ApplySprite(sprite, -BaseTileMap.GetColorInt(ref ether, 100));
                }
                RenderPreview();
            }
            catch
            {
            }
        }

        private void DrawSourceRenderer()
        {
            if (_sourceRenderer == null || _sourceRenderParam == null)
                return;

            var position = _sourceRenderPosition;
            _sourceRenderer.Draw(_sourceRenderParam, ref position, false);
            // This preview owns the renderer lifecycle. Keeping it outside the
            // global screen-sync list prevents the world renderer from
            // reclaiming it between UI and map render phases.
            RenderObject.syncList.Remove(_sourceRenderer);
        }

        private void ApplySprite(Sprite sprite, int etherMatColor)
        {
            if (_previewRenderer == null || _propertyBlock == null)
                return;

            _lastSprite = sprite;
            _previewRenderer.sprite = sprite;
            _propertyBlock.SetTexture("_MainTex", sprite.texture);
            _propertyBlock.SetFloat("_MatColor", etherMatColor);
            _propertyBlock.SetFloat("_Color", _sourceRenderParam?.color ?? 11010048f);
            _propertyBlock.SetFloat("_Liquid", 0f);
            var rect = sprite.textureRect;
            _propertyBlock.SetVector(
                "_Rect",
                new Vector4(
                    rect.xMin / sprite.texture.width,
                    rect.yMin / sprite.texture.height,
                    rect.xMax / sprite.texture.width,
                    rect.yMax / sprite.texture.height));
            _propertyBlock.SetFloat("_PixelHeight", sprite.rect.height);
            _previewRenderer.SetPropertyBlock(_propertyBlock);
            if (_fallback != null)
                _fallback.sprite = sprite;
        }

        private void RenderPreview()
        {
            if (_camera == null || _renderTexture == null)
                return;

            var bounds = _usesOriginalTilePass && _previewMeshRenderer != null
                ? _previewMeshRenderer.bounds
                : _previewRenderer != null
                    ? _previewRenderer.bounds
                    : default;
            if (bounds.size.x <= 0f || bounds.size.y <= 0f)
                return;
            // Image.preserveAspect fills the specimen slot without extra
            // framing. Match that fill ratio for the card preview as well.
            var padding = 1f;
            _camera.orthographicSize = Math.Max(bounds.extents.y, bounds.extents.x) * padding;
            _camera.transform.position = new Vector3(bounds.center.x, bounds.center.y, bounds.center.z - 10f);
            _camera.transform.rotation = Quaternion.identity;
            _camera.Render();
        }

        private static bool IsSamePlacementModel(Sprite placementSprite, Sprite actorSprite)
        {
            if (placementSprite == actorSprite)
                return true;
            if (placementSprite.texture != actorSprite.texture)
                return false;

            try
            {
                var placementRect = placementSprite.textureRect;
                var actorRect = actorSprite.textureRect;
                return Mathf.Abs(placementRect.x - actorRect.x) < 0.5f
                    && Mathf.Abs(placementRect.y - actorRect.y) < 0.5f
                    && Mathf.Abs(placementRect.width - actorRect.width) < 0.5f
                    && Mathf.Abs(placementRect.height - actorRect.height) < 0.5f;
            }
            catch
            {
                return false;
            }
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        private void Cleanup()
        {
            if (_cleaned)
                return;
            _cleaned = true;

            if (_target != null)
            {
                _target.material = null;
                _target.texture = null;
            }
            if (_fallbackHidden && _fallback != null)
            {
                _fallback.enabled = true;
                _fallbackHidden = false;
            }
            if (_camera != null)
                _camera.targetTexture = null;
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }
            if (_cameraObject != null)
                Destroy(_cameraObject);
            if (_previewObject != null)
                Destroy(_previewObject);
            ReleaseSourceRenderer();
            _camera = null;
            _cameraObject = null;
            _previewMeshRenderer = null;
            _previewMeshFilter = null;
            _previewRenderer = null;
            _previewObject = null;
            _propertyBlock = null;
            _lastSprite = null;
        }

        private void ReleaseSourceRenderer()
        {
            if (_sourceRenderer != null)
            {
                try
                {
                    RenderObject.syncList.Remove(_sourceRenderer);
                    _sourceRenderer.OnLeaveScreen();
                }
                catch
                {
                }
            }
            _sourceActor = null;
            _sourceRenderer = null;
            _sourceRenderParam = null;
        }
    }

    private sealed class LGuiNpcSpecimenPreview : MonoBehaviour
    {
        private const int PreviewLayer = 30;

        private Image? _fallback;
        private RawImage? _target;
        private GameObject? _previewObject;
        private SpriteRenderer? _spriteRenderer;
        private GameObject? _cameraObject;
        private Camera? _camera;
        private RenderTexture? _renderTexture;
        private bool _fallbackHidden;
        private bool _cleaned;

        public bool Initialize(Image fallback, RawImage target, Sprite sprite)
        {
            try
            {
                _fallback = fallback;
                _target = target;
                _previewObject = new GameObject("ElinModifierNpcSpecimenPreviewModel")
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    layer = PreviewLayer
                };
                _previewObject.transform.position = new Vector3(30000f, 30000f, 0f);
                // Use the source Sprite without the map MeshPass/material
                // branch. The latter applies world shading and makes the
                // specimen substantially darker than the original image.
                _spriteRenderer = _previewObject.AddComponent<SpriteRenderer>();
                _spriteRenderer.sprite = sprite;
                _spriteRenderer.color = Color.white;

                _cameraObject = new GameObject("ElinModifierNpcSpecimenPreviewCamera")
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    layer = PreviewLayer
                };
                _camera = _cameraObject.AddComponent<Camera>();
                _camera.enabled = false;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = Color.clear;
                _camera.orthographic = true;
                _camera.allowHDR = false;
                _camera.allowMSAA = false;
                _camera.useOcclusionCulling = false;
                _camera.cullingMask = 1 << PreviewLayer;

                _renderTexture = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32)
                {
                    name = "ElinModifierNpcSpecimenPreviewTexture",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    antiAliasing = 1
                };
                _renderTexture.Create();
                _camera.targetTexture = _renderTexture;
                _target.texture = _renderTexture;
                _target.material = null;
                _target.uvRect = new Rect(0f, 0f, 1f, 1f);
                _target.color = Color.white;
                _target.raycastTarget = false;

                if (!RenderPreview())
                    return false;
                _fallback.enabled = false;
                _fallbackHidden = true;
                return true;
            }
            catch
            {
                Cleanup();
                return false;
            }
        }

        private bool RenderPreview()
        {
            if (_camera == null || _renderTexture == null)
                return false;
            var bounds = _spriteRenderer != null ? _spriteRenderer.bounds : default;
            if (bounds.size.x <= 0f || bounds.size.y <= 0f)
                return false;

            _camera.orthographicSize = Math.Max(bounds.extents.y, bounds.extents.x);
            _camera.transform.position = new Vector3(bounds.center.x, bounds.center.y, bounds.center.z - 10f);
            _camera.transform.rotation = Quaternion.identity;
            _camera.Render();
            return true;
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        private void Cleanup()
        {
            if (_cleaned)
                return;
            _cleaned = true;
            if (_target != null)
            {
                _target.material = null;
                _target.texture = null;
            }
            if (_fallbackHidden && _fallback != null)
            {
                _fallback.enabled = true;
                _fallbackHidden = false;
            }
            if (_camera != null)
                _camera.targetTexture = null;
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }
            if (_cameraObject != null)
                Destroy(_cameraObject);
            if (_previewObject != null)
                Destroy(_previewObject);
            _camera = null;
            _cameraObject = null;
            _spriteRenderer = null;
            _previewObject = null;
        }
    }

    private void CreateLGuiNpcPlacementModels(RectTransform content, NpcRecord npc, float y)
    {
        const float panelX = 780f;
        const float panelWidth = 580f;
        const float cardWidth = 280f;
        const float cardHeight = 316f;

        var sprite = GetLGuiNpcPlacementSprite(npc);
        var cardColor = GetLGuiNpcCardPlacementColor();
        CreateLGuiNpcPlacementModelCard(
            content,
            "NpcFigurePlacementModel",
            T("标本", "Figure"),
            panelX,
            y,
            cardWidth,
            cardHeight,
            sprite,
            Color.white,
            npc,
            false);
        CreateLGuiNpcPlacementModelCard(
            content,
            "NpcCardPlacementModel",
            T("卡片", "Card"),
            panelX + panelWidth - cardWidth,
            y,
            cardWidth,
            cardHeight,
            sprite,
            cardColor,
            npc,
            true);
    }

    private void CreateLGuiNpcPlacementModelCard(
        RectTransform content,
        string name,
        string title,
        float x,
        float y,
        float width,
        float height,
        Sprite? sprite,
        Color modelColor,
        NpcRecord npc,
        bool cardPreview)
    {
        var background = CreateLGuiImage(content, name + "Background", x, y, width, height);
        // Preview cards are a single visual group. Do not reuse alternating
        // table-row colors here or the two cards look like zebra-striped rows.
        background.color = GetLGuiRowColor(0, false);
        background.raycastTarget = false;
        RegisterLGuiRoundedImage(background);

        var titleText = CreateLGuiText(background.transform, name + "Title", title, 17, TextAnchor.MiddleCenter, FontStyle.Normal);
        PlaceLGuiRect(titleText.rectTransform, 12f, 8f, width - 24f, 36f);
        titleText.raycastTarget = false;

        if (sprite != null)
        {
            var model = CreateLGuiImage(background.transform, name + "Image", 22f, 48f, width - 44f, height - 66f);
            model.sprite = sprite;
            model.preserveAspect = true;
            model.color = modelColor;
            model.raycastTarget = false;
            if (cardPreview && TryCreateLGuiOriginalCardPreview(
                    background.transform,
                    name,
                    22f,
                    48f,
                    width - 44f,
                    height - 66f,
                    npc,
                    sprite,
                    model))
                return;

            TryCreateLGuiSpecimenPreview(
                background.transform,
                name,
                22f,
                48f,
                width - 44f,
                height - 66f,
                sprite,
                model);
            return;
        }

        var unavailable = CreateLGuiText(
            background.transform,
            name + "Unavailable",
            T("无可用放置模型", "Placement model unavailable"),
            14,
            TextAnchor.MiddleCenter,
            FontStyle.Normal);
        PlaceLGuiRect(unavailable.rectTransform, 18f, 58f, width - 36f, height - 78f);
        unavailable.raycastTarget = false;
    }

    private Sprite? GetLGuiNpcPlacementSprite(NpcRecord npc)
    {
        try
        {
            var idSkin = 0;
            try
            {
                if (GameAccess.Runtime.Core?.config?.game?.antiSpider == true && npc.Row.skinAntiSpider != 0)
                    idSkin = npc.Row.skinAntiSpider;
            }
            catch
            {
            }
            return npc.Row.GetSprite(0, idSkin, false);
        }
        catch
        {
            return null;
        }
    }

    private bool TryCreateLGuiSpecimenPreview(
        Transform parent,
        string name,
        float x,
        float y,
        float width,
        float height,
        Sprite sprite,
        Image fallback)
    {
        var rect = CreateLGuiRect(parent, name + "SpecimenMaterial");
        var previewSize = Math.Min(width, height);
        PlaceLGuiRect(
            rect,
            x + (width - previewSize) * 0.5f,
            y + (height - previewSize) * 0.5f,
            previewSize,
            previewSize);
        var rawImage = rect.gameObject.AddComponent<RawImage>();
        var preview = rect.gameObject.AddComponent<LGuiNpcSpecimenPreview>();
        if (preview.Initialize(fallback, rawImage, sprite))
            return true;

        Destroy(rect.gameObject);
        return false;
    }

    private bool TryCreateLGuiOriginalCardPreview(
        Transform parent,
        string name,
        float x,
        float y,
        float width,
        float height,
        NpcRecord npc,
        Sprite sprite,
        Image fallback)
    {
        var rect = CreateLGuiRect(parent, name + "OriginalMaterial");
        var previewSize = Math.Min(width, height);
        PlaceLGuiRect(
            rect,
            x + (width - previewSize) * 0.5f,
            y + (height - previewSize) * 0.5f,
            previewSize,
            previewSize);
        var rawImage = rect.gameObject.AddComponent<RawImage>();
        var preview = rect.gameObject.AddComponent<LGuiNpcOriginalCardPreview>();
        if (preview.Initialize(fallback, rawImage, npc.Row, sprite, npc.Id))
            return true;

        // If the row has no real character placement model, keep the same
        // neutral specimen image instead of tinting its placeholder as a card.
        fallback.color = Color.white;
        Destroy(rect.gameObject);
        return false;
    }

    private static Color GetLGuiNpcCardPlacementColor()
    {
        try
        {
            var color = GameAccess.Runtime.Core.Colors.matColors["ether"].main;
            color.a = 1f;
            return color;
        }
        catch
        {
            return new Color(0.48f, 0.88f, 0.82f, 1f);
        }
    }
}
