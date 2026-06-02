#ifndef PCH_H
#define PCH_H

#include "framework.h"

#include <filesystem>
#include <string>

#include <shlobj_core.h>
#include <shlwapi.h>
#include <ocidl.h>
#include <servprov.h>
#pragma comment(lib, "shlwapi.lib")

#include <winrt/Windows.Foundation.Collections.h>

#pragma warning(push)
// WIL workaround per https://github.com/microsoft/wil/issues/610.
#pragma warning(disable: 28182)
#include <wil/stl.h>
#include <wil/win32_helpers.h>
#pragma warning(pop)

#endif //PCH_H
