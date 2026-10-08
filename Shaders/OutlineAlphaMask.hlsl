#ifndef BILL_OUTLINE_ALPHA_MASK_INCLUDED
#define BILL_OUTLINE_ALPHA_MASK_INCLUDED

// Outline mask carried in the colour buffer's alpha. Without it every outlined object is drawn a
// second time into the mask texture, which is expensive for large crowds. Shaders that include this
// file and call BillOutlineAlpha() write alpha 0 in their normal colour pass when their renderer is on one of
// these rendering layers, and everything else writes 1. The outline composite reads 1 - alpha as
// the mask, before the transparents can blend into it.
//
// Set per camera by OutlineFeature: 0 when the path is off (scene view, captures into textures,
// a colour format without alpha), so those keep their ordinary alpha.
float _BillOutlineAlphaLayers;

half BillOutlineAlpha(half alpha)
{
    uint layers = (uint)_BillOutlineAlphaLayers;
    if (layers == 0u) return alpha;
    return (GetMeshRenderingLayer() & layers) != 0u ? 0.0h : 1.0h;
}

#endif
