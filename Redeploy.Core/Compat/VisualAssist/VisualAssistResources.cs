using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Redeploy.Core.Compat;

/// <summary>Only references supplied by this exact arc are owned; never enumerate scene renderers.</summary>
internal sealed class VisualAssistResources
{
    private readonly LineRenderer _line;
    private readonly GameObject _sphere;
    private readonly Renderer _sphereRenderer;
    private readonly Material _lineMaterial, _sphereMaterial;
    private bool _released;

    internal VisualAssistResources(VisualAssistContract contract, Component arc, EFT.Player body, bool unassignedCreation = false, bool ownMaterials = true)
    {
        if (!ReferenceEquals(arc.gameObject, body.gameObject)
            || (!ReferenceEquals(contract.Player.GetValue(arc), body)
                && !(unassignedCreation && ReferenceEquals(contract.Player.GetValue(arc), null))))
            throw new InvalidOperationException("VisualAssist arc belongs to another physical body");
        _line = contract.Line.GetValue(arc) as LineRenderer;
        _sphere = contract.Sphere.GetValue(arc) as GameObject;
        _sphereRenderer = contract.SphereRenderer.GetValue(arc) as Renderer;
        if ((_line != null && !ReferenceEquals(_line.gameObject, body.gameObject))
            || (_sphereRenderer != null && !ReferenceEquals(_sphereRenderer.gameObject, _sphere))
            || ReferenceEquals(_sphere, body.gameObject))
            throw new InvalidOperationException("VisualAssist rendering ownership changed");
        // sharedMaterial reads the exact native material; material would instantiate another copy.
        // During a failed Awake these may still be shared primitive defaults. Only a
        // completed native initializer proves that both new materials were assigned.
        _lineMaterial = ownMaterials && _line != null ? _line.sharedMaterial : null;
        _sphereMaterial = ownMaterials && _sphereRenderer != null ? _sphereRenderer.sharedMaterial : null;
    }
    internal void Hide()
    {
        if (_line != null) { _line.enabled = false; _line.positionCount = 0; }
        if (_sphereRenderer != null) _sphereRenderer.enabled = false;
    }
    internal void Release()
    {
        if (_released) return;
        Hide();
        if (_line != null) Object.DestroyImmediate(_line);
        if (_sphere != null) Object.DestroyImmediate(_sphere);
        if (_lineMaterial != null) Object.DestroyImmediate(_lineMaterial);
        if (_sphereMaterial != null && !ReferenceEquals(_sphereMaterial, _lineMaterial)) Object.DestroyImmediate(_sphereMaterial);
        _released = true;
    }
}
