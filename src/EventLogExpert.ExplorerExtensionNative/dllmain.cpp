// IExplorerCommand shell extension. Packaged COM in-proc handler loaded by dllhost.exe via
// <com:SurrogateServer> in Package.appxmanifest; Explorer activates context-menu handlers via
// CLSCTX_INPROC_HANDLER, so a DLL is required (an EXE COM server is never queried).
//
// CLSID below MUST match the manifest <com:Class Id="...">, all desktop4/desktop5 verb Clsid
// attributes, and the kCanonical literal in GetCanonicalName. Stable across releases.

#include "pch.h"

#define CLSID_UUID "F1B2C3D4-E5F6-4789-AB12-CD34EF567890"

constexpr const wchar_t* menu_entry_title = L"Open with EventLogExpert";
constexpr const wchar_t* exe_filename = L"eventlogexpert.exe";
constexpr const wchar_t* evtx_extension = L".evtx";

BOOL APIENTRY DllMain(_In_ HMODULE hModule, _In_ DWORD ul_reason_for_call, _In_opt_ LPVOID lpReserved) {
    UNREFERENCED_PARAMETER(hModule);
    UNREFERENCED_PARAMETER(lpReserved);
    switch (ul_reason_for_call) {
    case DLL_PROCESS_ATTACH:
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}

namespace {

    // CommandLineToArgvW-compatible quoting per
    // https://learn.microsoft.com/cpp/cpp/main-function-command-line-args#parsing-c-command-line-arguments.
    std::wstring QuoteForCommandLineArg(_In_ const std::wstring& arg) {
        // Includes ASCII whitespace beyond plain space — CreateProcessW splits on tab/CR/LF too.
        const std::wstring quotable_chars(L" \t\r\n\\\"");

        if (arg.find_first_of(quotable_chars) == std::wstring::npos) {
            return arg;
        }

        std::wstring out;
        out.push_back('"');

        for (size_t i = 0; i < arg.size(); ++i) {
            if (arg[i] == '\\') {
                const size_t start = i;
                size_t end = start + 1;

                for (; end < arg.size() && arg[end] == '\\'; ++end) {}

                size_t backslash_count = end - start;

                if (end == arg.size() || arg[end] == '"') {
                    backslash_count *= 2;
                }

                for (size_t j = 0; j < backslash_count; ++j)
                    out.push_back('\\');

                i = end - 1;
            }
            else if (arg[i] == '"') {
                out.push_back('\\');
                out.push_back('"');
            }
            else {
                out.push_back(arg[i]);
            }
        }
        out.push_back('"');

        return out;
    }

    bool HasEvtxExtension(_In_ const std::wstring& path) noexcept {
        try {
            auto ext = std::filesystem::path{path}.extension().wstring();

            if (ext.size() != wcslen(evtx_extension)) return false;

            return _wcsicmp(ext.c_str(), evtx_extension) == 0;
        }
        catch (...) {
            return false;
        }
    }

    bool IsDirectoryOnDisk(_In_ const std::wstring& path) noexcept {
        try {
            std::error_code ec;

            return std::filesystem::is_directory(path, ec) && !ec;
        }
        catch (...) {
            return false;
        }
    }

    // Pre-filter for folder Invoke: skip empty folders (no .evtx children); pass through
    // enumeration failures so the launched app's alert path runs instead of silently dropping.
    bool ShouldOpenFolder(_In_ const std::wstring& folder) noexcept {
        try {
            if (!std::filesystem::is_directory(folder)) return false;

            bool has_evtx = false;
            bool enum_ok = true;
            std::error_code ec;
            auto begin = std::filesystem::directory_iterator(folder, std::filesystem::directory_options::skip_permission_denied, ec);

            if (ec) {
                enum_ok = false;
            } else {
                for (auto it = begin; it != std::filesystem::directory_iterator(); it.increment(ec)) {
                    if (ec) { enum_ok = false; break; }

                    if (!it->is_regular_file(ec)) continue;

                    auto ext = it->path().extension().wstring();

                    if (ext.size() == wcslen(evtx_extension) && _wcsicmp(ext.c_str(), evtx_extension) == 0) {
                        has_evtx = true;

                        break;
                    }
                }
            }
            // Open ONLY when (a) enumeration succeeded AND found at least one .evtx, OR (b)
            // enumeration failed (let the receiving app surface its alert).
            return !enum_ok || has_evtx;
        }
        catch (...) {
            return true;
        }
    }

    // wil::GetModuleInstanceHandle returns THIS DLL's module — sibling exe_filename in the same
    // directory is the install location.
    std::filesystem::path GetMainExePath() {
        std::filesystem::path module_path{wil::GetModuleFileNameW<std::wstring>(wil::GetModuleInstanceHandle())};
        module_path.remove_filename();
        module_path /= exe_filename;

        return module_path;
    }

    void StripTrailingSeparators(std::wstring& path) noexcept {
        // Preserve drive root: "C:\" stays "C:\" instead of becoming the drive-relative "C:".
        while (!path.empty() && (path.back() == L'\\' || path.back() == L'/')) {
            if (path.size() == 3 && path[1] == L':') break;

            path.pop_back();
        }
    }

} // namespace

struct ExplorerCommandHandler : public winrt::implements<ExplorerCommandHandler, IExplorerCommand, IObjectWithSite> {
public:
    // IObjectWithSite — required for Directory\Background context-menu surface where the shell
    // passes items=null and the handler must recover the current folder via the site's IShellView.
    IFACEMETHODIMP SetSite(_In_opt_ IUnknown* pUnkSite) noexcept override {
        // copy_from handles both null (releases site) and same-pointer reassignment safely;
        // a manual nullptr-then-AddRef pattern would destroy the COM object if pUnkSite holds
        // the only live reference.
        m_site.copy_from(pUnkSite);

        return S_OK;
    }

    IFACEMETHODIMP GetSite(_In_ REFIID riid, _COM_Outptr_ void** ppvSite) noexcept override {
        if (!ppvSite) return E_POINTER;

        *ppvSite = nullptr;

        if (!m_site) return E_FAIL;

        return m_site->QueryInterface(riid, ppvSite);
    }

    IFACEMETHODIMP GetTitle(_In_opt_ IShellItemArray* items, _Outptr_ PWSTR* name) override {
        UNREFERENCED_PARAMETER(items);

        RETURN_HR_IF_NULL(E_POINTER, name);
        *name = nullptr;

        return SHStrDupW(menu_entry_title, name);
    }

    IFACEMETHODIMP GetIcon(_In_opt_ IShellItemArray* items, _Outptr_ PWSTR* icon) override {
        UNREFERENCED_PARAMETER(items);

        RETURN_HR_IF_NULL(E_POINTER, icon);
        *icon = nullptr;

        auto exe_path = GetMainExePath();

        return SHStrDupW(exe_path.c_str(), icon);
    }

    IFACEMETHODIMP GetToolTip(_In_opt_ IShellItemArray* items, _Outptr_ PWSTR* infoTip) override {
        UNREFERENCED_PARAMETER(items);

        RETURN_HR_IF_NULL(E_POINTER, infoTip);
        *infoTip = nullptr;

        return E_NOTIMPL;
    }

    IFACEMETHODIMP GetCanonicalName(_Out_ GUID* guidCommandName) override {
        RETURN_HR_IF_NULL(E_POINTER, guidCommandName);

        // Literal GUID (not __uuidof) because DECLSPEC_UUID is on ClassFactory, not on this type.
        constexpr GUID kCanonical = {0xF1B2C3D4, 0xE5F6, 0x4789, {0xAB, 0x12, 0xCD, 0x34, 0xEF, 0x56, 0x78, 0x90}};
        *guidCommandName = kCanonical;

        return S_OK;
    }

    IFACEMETHODIMP GetState(_In_opt_ IShellItemArray* items, _In_ BOOL okToBeSlow, _Out_ EXPCMDSTATE* cmdState) override {
        RETURN_HR_IF_NULL(E_POINTER, cmdState);

        // Default hidden: the manifest registers Type="*" so every file right-click invokes us.
        *cmdState = ECS_HIDDEN;

        if (!items) {
            // Background-menu surface — recover folder from site.
            auto folder = GetFolderFromSite();

            if (folder.empty()) return S_OK;

            // Cheap path: enable when fast-render is requested; Invoke does the actual evtx check.
            if (!okToBeSlow) {
                *cmdState = ECS_ENABLED;

                return S_OK;
            }

            if (ShouldOpenFolder(folder)) {
                *cmdState = ECS_ENABLED;
            }

            return S_OK;
        }

        DWORD count = 0;
        if (FAILED(items->GetCount(&count)) || count == 0) return S_OK;

        for (DWORD i = 0; i < count; ++i) {
            winrt::com_ptr<IShellItem> item;

            if (FAILED(items->GetItemAt(i, item.put())) || !item) continue;

            // Cheap path: shell-cached folder attribute, no filesystem syscall in typical case.
            SFGAOF attr = 0;
            bool isFolder = SUCCEEDED(item->GetAttributes(SFGAO_FOLDER, &attr)) && (attr & SFGAO_FOLDER);

            if (isFolder) {
                if (!okToBeSlow) {
                    *cmdState = ECS_ENABLED;

                    return S_OK;
                }
                // okToBeSlow=true: probe the folder for at least one .evtx so empty folders don't
                // light up the verb (consistent with the background-menu surface).
                wil::unique_cotaskmem_string folderPath;

                if (FAILED(item->GetDisplayName(SIGDN_FILESYSPATH, &folderPath)) || !folderPath) continue;

                if (ShouldOpenFolder(folderPath.get())) {
                    *cmdState = ECS_ENABLED;

                    return S_OK;
                }
                continue;
            }

            wil::unique_cotaskmem_string path;

            if (FAILED(item->GetDisplayName(SIGDN_FILESYSPATH, &path)) || !path) continue;

            if (HasEvtxExtension(path.get())) {
                *cmdState = ECS_ENABLED;

                return S_OK;
            }
        }

        return S_OK;
    }

    IFACEMETHODIMP GetFlags(_Out_ EXPCMDFLAGS* flags) override {
        RETURN_HR_IF_NULL(E_POINTER, flags);

        *flags = ECF_DEFAULT;

        return S_OK;
    }

    IFACEMETHODIMP EnumSubCommands(_Outptr_ IEnumExplorerCommand** enumCommands) override {
        RETURN_HR_IF_NULL(E_POINTER, enumCommands);

        *enumCommands = nullptr;

        return E_NOTIMPL;
    }

    IFACEMETHODIMP Invoke(_In_opt_ IShellItemArray* items, _In_opt_ IBindCtx* bindCtx) override {
        UNREFERENCED_PARAMETER(bindCtx);

        auto exe_path = GetMainExePath();
        auto command = wil::str_printf<std::wstring>(L"\"%s\"", exe_path.c_str());
        bool any_added = false;

        if (!items) {
            // Background-menu surface — operate on the current folder from the site.
            auto folder = GetFolderFromSite();

            if (folder.empty()) return S_OK;

            if (!ShouldOpenFolder(folder)) return S_OK;

            std::wstring p{folder};
            StripTrailingSeparators(p);
            command = wil::str_printf<std::wstring>(L"%s %s", command.c_str(), QuoteForCommandLineArg(p).c_str());
            any_added = true;
        } else {
            DWORD count = 0;

            RETURN_IF_FAILED(items->GetCount(&count));

            if (count == 0) return S_OK;

            for (DWORD i = 0; i < count; ++i) {
                winrt::com_ptr<IShellItem> item;

                if (FAILED(items->GetItemAt(i, item.put())) || !item) continue;

                wil::unique_cotaskmem_string path;

                if (FAILED(item->GetDisplayName(SIGDN_FILESYSPATH, &path)) || !path) continue;

                std::wstring p{path.get()};
                bool is_dir = IsDirectoryOnDisk(p);

                if (!is_dir && !HasEvtxExtension(p)) continue;

                if (is_dir && !ShouldOpenFolder(p)) continue;

                StripTrailingSeparators(p);
                command = wil::str_printf<std::wstring>(L"%s %s", command.c_str(), QuoteForCommandLineArg(p).c_str());
                any_added = true;
            }
        }

        if (!any_added) return S_OK;

        wil::unique_process_information process_info;
        STARTUPINFOW startup_info = {sizeof(startup_info)};

        RETURN_IF_WIN32_BOOL_FALSE(CreateProcessW(
            nullptr,
            command.data(),
            nullptr,
            nullptr,
            FALSE,
            CREATE_NO_WINDOW,
            nullptr,
            nullptr,
            &startup_info,
            &process_info));

        return S_OK;
    }

private:
    winrt::com_ptr<IUnknown> m_site;

    // Resolve current folder from the site (Directory\Background surface). Returns empty string
    // on any failure — caller treats empty as "no folder, do nothing".
    std::wstring GetFolderFromSite() noexcept {
        try {
            if (!m_site) return {};

            winrt::com_ptr<IServiceProvider> sp;

            if (FAILED(m_site->QueryInterface(IID_PPV_ARGS(sp.put())))) return {};

            winrt::com_ptr<IFolderView> folderView;

            if (FAILED(sp->QueryService(SID_SFolderView, IID_PPV_ARGS(folderView.put())))) return {};

            winrt::com_ptr<IPersistFolder2> persistFolder;

            if (FAILED(folderView->GetFolder(IID_PPV_ARGS(persistFolder.put())))) return {};

            // Allocate buffer BEFORE GetCurFolder so a bad_alloc throw cannot leak the PIDL.
            // 32768 wchars covers the documented Windows absolute max path length (32767 + null).
            std::wstring buffer(32768, L'\0');

            PIDLIST_ABSOLUTE pidl = nullptr;

            if (FAILED(persistFolder->GetCurFolder(&pidl)) || !pidl) return {};

            // SHGetPathFromIDListEx + GPFIDL_DEFAULT preserves SHGetPathFromIDListW semantics
            // without the MAX_PATH stack-buffer ceiling.
            BOOL ok = SHGetPathFromIDListEx(pidl, buffer.data(), static_cast<DWORD>(buffer.size()), GPFIDL_DEFAULT);
            CoTaskMemFree(pidl);

            if (!ok) return {};

            buffer.resize(wcslen(buffer.c_str()));

            return buffer;
        }
        catch (...) {
            return {};
        }
    }
};

struct DECLSPEC_UUID(CLSID_UUID) ClassFactory : public winrt::implements<ClassFactory, IClassFactory> {
public:
    IFACEMETHODIMP CreateInstance(_In_opt_ IUnknown* pUnkOuter, _In_ REFIID riid, _COM_Outptr_ void** ppvObject) noexcept override {
        if (ppvObject) *ppvObject = nullptr;

        if (pUnkOuter) return CLASS_E_NOAGGREGATION;

        try {
            return winrt::make<ExplorerCommandHandler>()->QueryInterface(riid, ppvObject);
        }
        catch (...) {
            return winrt::to_hresult();
        }
    }

    IFACEMETHODIMP LockServer(_In_ BOOL fLock) noexcept override {
        if (fLock)
            ++winrt::get_module_lock();
        else
            --winrt::get_module_lock();
        return S_OK;
    }
};

_Check_return_
STDAPI DllGetClassObject(_In_ REFCLSID rclsid, _In_ REFIID riid, _Outptr_ LPVOID* ppv) {
    if (ppv == nullptr) return E_POINTER;

    *ppv = nullptr;

    if (rclsid != __uuidof(ClassFactory)) return CLASS_E_CLASSNOTAVAILABLE;

    if (riid != IID_IClassFactory && riid != IID_IUnknown) return E_NOINTERFACE;

    try {
        return winrt::make<ClassFactory>()->QueryInterface(riid, ppv);
    }
    catch (...) {
        return winrt::to_hresult();
    }
}

__control_entrypoint(DllExport)
STDAPI DllCanUnloadNow(void) {
    if (winrt::get_module_lock())
        return S_FALSE;

    winrt::clear_factory_cache();

    return S_OK;
}