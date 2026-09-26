// An orthonormal basis about a normal, which every pass that fires rays over a hemisphere needs and
// which all of them have to build the same way.
//
// Duff's branchless construction, returned as its three axes rather than as a matrix: a matrix
// constructor fills rows in this language and columns in the one these shaders came from, so writing the
// product out is both unambiguous and what keeps the association the original had.

#ifndef OPENIGI_HEMISPHERE_INCLUDED
#define OPENIGI_HEMISPHERE_INCLUDED

struct HemisphereBasis
{
    float3 tangent;
    float3 bitangent;
    float3 normal;
};

HemisphereBasis BasisAround(float3 normal)
{
    HemisphereBasis basis;
    basis.normal = normal;

    if (normal.z < -0.9999999)
    {
        basis.tangent = float3(0.0, -1.0, 0.0);
        basis.bitangent = float3(-1.0, 0.0, 0.0);
        return basis;
    }

    float a = 1.0 / (1.0 + normal.z);
    float b = -normal.x * normal.y * a;
    basis.tangent = float3(1.0 - (normal.x * normal.x * a), b, -normal.x);
    basis.bitangent = float3(b, 1.0 - (normal.y * normal.y * a), -normal.y);
    return basis;
}

// A direction given in the basis's own frame, taken into the frame the basis was built in.
float3 Orient(HemisphereBasis basis, float3 local)
{
    return (basis.tangent * local.x) + (basis.bitangent * local.y) + (basis.normal * local.z);
}

#endif
