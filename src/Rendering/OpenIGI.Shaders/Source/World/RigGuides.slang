// The first-person rig drawn a second time, into the guides DLSS ray reconstruction reads.
//
// The rig is drawn in the forward pass, after the G-buffer, so the guides built from the G-buffer describe
// whatever stands behind it. This is the rig's own shader in its guide form: the same vertex stage and the
// same alpha cut, writing the base colour and the view-space normal its key light starts from. The guide
// pass (Post/ReconstructionGuides.slang) takes those wherever the rig is in front, and turns them into the
// rig's material -- its one material, the lit stage's mix of glove and gun steel.

#define OPENIGI_MESH_GUIDES 1
#include "Mesh3D.slang"
