// SectorManager.h
//
// Phase J: Net7Proxy historically included server/src/SectorManager.h via
// the include search path. Proxy/ doesn't have its own SectorManager
// implementation — the include in Connection.h:13 only needs a forward
// declaration, never a full type, since Connection holds no SectorManager
// members. This stub satisfies the #include without dragging in the
// 162K-LOC server tree.

#ifndef _PROXY_SECTOR_MANAGER_H_INCLUDED_
#define _PROXY_SECTOR_MANAGER_H_INCLUDED_

class SectorManager;

#endif // _PROXY_SECTOR_MANAGER_H_INCLUDED_
