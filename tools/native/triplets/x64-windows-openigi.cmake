# libdatachannel as one DLL with everything it needs inside: OpenSSL, usrsctp, libjuice and the C runtime are linked in
# statically, so a player needs nothing installed beside the game.
set(VCPKG_TARGET_ARCHITECTURE x64)
set(VCPKG_CRT_LINKAGE static)
set(VCPKG_LIBRARY_LINKAGE static)
set(VCPKG_BUILD_TYPE release)
if(PORT STREQUAL "libdatachannel")
  set(VCPKG_LIBRARY_LINKAGE dynamic)
  # libjuice's random numbers come from bcrypt, which its shared build links itself and its static one leaves to us.
  set(VCPKG_LINKER_FLAGS "bcrypt.lib")
endif()
