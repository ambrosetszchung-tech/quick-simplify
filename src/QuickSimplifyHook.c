#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <imm.h>
#include <msctf.h>
#include <stdio.h>
#include <string.h>
#include "MinHook.h"
// The controller creates a per-process mapping before installing this hook.
// Without that mapping, the DLL does not patch the target process.
typedef struct {
    DWORD targetPid,controllerPid,lastKey,enabled,state,calls,conversions;
    LONG activationHr;
    WCHAR original[128],converted[128];
    DWORD stop;
    DWORD focusHwnd,immContext,immOpenBefore,immOpenAfter;
    DWORD activeLang,activeType,activeQuick,keyboardLayout,activeHr;
    DWORD conversionBefore,conversionAfter,conversionSet;
    DWORD production;
} ProbeState;
typedef HRESULT (WINAPI *SETTEXT)(void*,DWORD,DWORD,const WCHAR*,LONG);
static ProbeState *state;
static HANDLE mapping;
static SETTEXT originalSetText;
static LONG initialized;
static BOOL QuickActive(void){
    typedef HRESULT (WINAPI *CREATE)(void**);
    typedef HRESULT (WINAPI *QI)(void*,const GUID*,void**);
    typedef ULONG (WINAPI *RELEASE)(void*);
    typedef HRESULT (WINAPI *GETACTIVE)(void*,const GUID*,TF_INPUTPROCESSORPROFILE*);
    GUID iid={0x71c6e74c,0x0f28,0x11d8,{0xa8,0x2a,0x00,0x06,0x5b,0x84,0x43,0x5c}};
    GUID cls={0x531fdebf,0x9b4c,0x4a43,{0xa2,0xaa,0x96,0x0e,0x8f,0xcd,0xc7,0x32}};
    GUID profile={0x6024b45f,0x5c54,0x11d4,{0xb9,0x21,0x00,0x80,0xc8,0x82,0x68,0x7e}};
    GUID cat={0x34745c63,0xb2f0,0x4784,{0x8b,0x67,0x5e,0x12,0xc8,0x70,0x1a,0x31}};
    HMODULE msctf=GetModuleHandleW(L"msctf.dll");if(!msctf)return FALSE;
    CREATE create=(CREATE)GetProcAddress(msctf,"TF_CreateInputProcessorProfiles");if(!create)return FALSE;
    void *p=NULL,*m=NULL;HRESULT hr=create(&p);if(FAILED(hr))return FALSE;
    void** v=*(void***)p;hr=((QI)v[0])(p,&iid,&m);
    BOOL quick=FALSE;
    if(SUCCEEDED(hr)){
        void** vm=*(void***)m;TF_INPUTPROCESSORPROFILE active={0};
        hr=((GETACTIVE)vm[10])(m,&cat,&active);state->activeHr=(DWORD)hr;
        if(SUCCEEDED(hr)){state->activeLang=active.langid;state->activeType=active.dwProfileType;quick=IsEqualGUID(&active.clsid,&cls)&&IsEqualGUID(&active.guidProfile,&profile);state->activeQuick=quick;}
        ((RELEASE)vm[2])(m);
    }
    ((RELEASE)v[2])(p);return quick;
}
static HRESULT WINAPI ProbeSetText(void* self,DWORD cookie,DWORD flags,const WCHAR* text,LONG length){
    InterlockedIncrement((volatile LONG*)&state->calls);
    DWORD key=state->lastKey;
    BOOL confirm=(key>=0x31&&key<=0x39)||key==VK_RETURN;
    LONG count=text?(length<0?(LONG)wcslen(text):length):0;
    if(state->enabled&&confirm&&count>0&&count<4096&&(!state->production||QuickActive())){
        WCHAR* converted=HeapAlloc(GetProcessHeap(),0,(count+1)*sizeof(WCHAR));
        if(converted){
            int got=LCMapStringEx(L"zh-CN",LCMAP_SIMPLIFIED_CHINESE,text,count,converted,count,NULL,NULL,0);
            if(got==count&&memcmp(text,converted,count*sizeof(WCHAR))!=0){
                converted[count]=0;
                if(!state->production){int save=count<127?count:127;
                    memcpy(state->original,text,save*sizeof(WCHAR));state->original[save]=0;
                    memcpy(state->converted,converted,save*sizeof(WCHAR));state->converted[save]=0;}
                InterlockedIncrement((volatile LONG*)&state->conversions);
                HRESULT hr=originalSetText(self,cookie,flags,converted,length);
                state->lastKey=0;
                HeapFree(GetProcessHeap(),0,converted);return hr;
            }
            HeapFree(GetProcessHeap(),0,converted);
        }
    }
    return originalSetText(self,cookie,flags,text,length);
}
static DWORD SetTextRvaForModule(HMODULE module){
    // These CodeView identities and RVAs are paired. A new Windows build must
    // be analyzed before its private CRange::_SetText entry can be hooked.
    static const struct { BYTE guid[16]; DWORD age, rva; } versions[]={
        {{0xa7,0xc0,0xa2,0x34,0x23,0xda,0x2e,0x21,0x70,0x02,0x41,0x81,0xad,0xfb,0x5b,0x3c},1,0x2aed8},
        {{0xdd,0x43,0x23,0x60,0x1c,0x7a,0x8e,0x98,0x2e,0x58,0x53,0x78,0xba,0xc5,0x77,0x0a},1,0x2ad58}
    };
    BYTE* base=(BYTE*)module;IMAGE_DOS_HEADER* dos=(IMAGE_DOS_HEADER*)base;
    if(dos->e_magic!=IMAGE_DOS_SIGNATURE)return 0;
    IMAGE_NT_HEADERS64* nt=(IMAGE_NT_HEADERS64*)(base+dos->e_lfanew);
    if(nt->Signature!=IMAGE_NT_SIGNATURE||nt->OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR64_MAGIC)return 0;
    IMAGE_DATA_DIRECTORY directory=nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DEBUG];
    if(!directory.VirtualAddress||directory.Size<sizeof(IMAGE_DEBUG_DIRECTORY))return 0;
    IMAGE_DEBUG_DIRECTORY* debug=(IMAGE_DEBUG_DIRECTORY*)(base+directory.VirtualAddress);
    for(DWORD i=0;i<directory.Size/sizeof(*debug);i++){
        if(debug[i].Type==IMAGE_DEBUG_TYPE_CODEVIEW&&debug[i].SizeOfData>=24){
            BYTE* cv=base+debug[i].AddressOfRawData;
            if(memcmp(cv,"RSDS",4)!=0)continue;
            for(DWORD j=0;j<sizeof(versions)/sizeof(versions[0]);j++)
                if(memcmp(cv+4,versions[j].guid,16)==0&&*(DWORD*)(cv+20)==versions[j].age)
                    return versions[j].rva;
        }
    }
    return 0;
}
static HRESULT ActivateOwnQuick(void){
    typedef HRESULT (WINAPI *CREATE)(void**);
    typedef HRESULT (WINAPI *QI)(void*,const GUID*,void**);
    typedef ULONG (WINAPI *RELEASE)(void*);
    typedef HRESULT (WINAPI *ACTIVATE)(void*,DWORD,LANGID,const GUID*,const GUID*,HKL,DWORD);
    GUID iid={0x71c6e74c,0x0f28,0x11d8,{0xa8,0x2a,0x00,0x06,0x5b,0x84,0x43,0x5c}};
    GUID cls={0x531fdebf,0x9b4c,0x4a43,{0xa2,0xaa,0x96,0x0e,0x8f,0xcd,0xc7,0x32}};
    GUID profile={0x6024b45f,0x5c54,0x11d4,{0xb9,0x21,0x00,0x80,0xc8,0x82,0x68,0x7e}};
    GUID cat={0x34745c63,0xb2f0,0x4784,{0x8b,0x67,0x5e,0x12,0xc8,0x70,0x1a,0x31}};
    HMODULE msctf=GetModuleHandleW(L"msctf.dll");if(!msctf)return E_FAIL;
    CREATE create=(CREATE)GetProcAddress(msctf,"TF_CreateInputProcessorProfiles");if(!create)return E_FAIL;
    void* p=NULL;void* m=NULL;HRESULT hr=create(&p);if(FAILED(hr))return hr;
    void** v=*(void***)p;hr=((QI)v[0])(p,&iid,&m);
    if(SUCCEEDED(hr)){void** vm=*(void***)m;hr=((ACTIVATE)vm[3])(m,1,0x404,&cls,&profile,0,0x10000000);
        TF_INPUTPROCESSORPROFILE active={0};
        typedef HRESULT (WINAPI *GETACTIVE)(void*,const GUID*,TF_INPUTPROCESSORPROFILE*);
        HRESULT ahr=((GETACTIVE)vm[10])(m,&cat,&active);
        state->activeHr=(DWORD)ahr;
        if(SUCCEEDED(ahr)){state->activeLang=active.langid;state->activeType=active.dwProfileType;state->activeQuick=IsEqualGUID(&active.clsid,&cls)&&IsEqualGUID(&active.guidProfile,&profile);}
        ((RELEASE)vm[2])(m);}
    ((RELEASE)v[2])(p);return hr;
}
static void BeginOnce(void){
    if(!state){WCHAR name[80];swprintf(name,80,L"Local\\QuickNativeProbe_%lu",GetCurrentProcessId());mapping=OpenFileMappingW(FILE_MAP_READ|FILE_MAP_WRITE,FALSE,name);if(!mapping)return;state=MapViewOfFile(mapping,FILE_MAP_READ|FILE_MAP_WRITE,0,0,sizeof(ProbeState));if(!state||state->targetPid!=GetCurrentProcessId())return;}
    if(state->stop){if(initialized==1){MH_DisableHook(MH_ALL_HOOKS);MH_Uninitialize();initialized=2;}state->state=90;return;}
    HMODULE module=GetModuleHandleW(L"TextInputFramework.dll");if(!module)return;
    if(InterlockedCompareExchange(&initialized,1,0)!=0)return;
    DWORD setTextRva=SetTextRvaForModule(module);
    if(!setTextRva){state->state=20;return;}
    if(state->production==1){QuickActive();}else{state->activationHr=ActivateOwnQuick();}
    state->keyboardLayout=(DWORD)(ULONG_PTR)GetKeyboardLayout(0);
    HWND focus=GetFocus();
    state->focusHwnd=(DWORD)(ULONG_PTR)focus;
    if(focus&&state->production!=1){
        HIMC context=ImmGetContext(focus);
        if(context){
            state->immContext=1;
            state->immOpenBefore=ImmGetOpenStatus(context);
            ImmSetOpenStatus(context,TRUE);
            state->immOpenAfter=ImmGetOpenStatus(context);
            DWORD mode=0,sentence=0;
            if(ImmGetConversionStatus(context,&mode,&sentence)){
                state->conversionBefore=mode;
                state->conversionSet=ImmSetConversionStatus(context,mode|IME_CMODE_NATIVE,sentence);
                if(ImmGetConversionStatus(context,&mode,&sentence))state->conversionAfter=mode;
            }
            ImmReleaseContext(focus,context);
        }
    }
    MH_STATUS init=MH_Initialize();if(init!=MH_OK){state->state=30+init;return;}
    MH_STATUS create=MH_CreateHook((BYTE*)module+setTextRva,(void*)ProbeSetText,(void**)&originalSetText);
    if(create!=MH_OK){state->state=50+create;return;}
    MH_STATUS enable=MH_EnableHook((BYTE*)module+setTextRva);
    state->state=enable==MH_OK?1:70+enable;
}
__declspec(dllexport) LRESULT CALLBACK ProbeMessageHook(int code,WPARAM w,LPARAM l){if(code>=0)BeginOnce();return CallNextHookEx(NULL,code,w,l);}
BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,LPVOID reserved){if(reason==DLL_PROCESS_ATTACH)DisableThreadLibraryCalls(module);return TRUE;}
