namespace PcSetup
{
    // 화면 단추가 부르는 기능 — 각 파일(UI\AddApp.cs·UI\Upload.cs·Upgrade.cs·Updater.cs)이 채운다
    public static partial class Hooks
    {
        // 앱 추가… (winget 에서 찾아 catalog.txt 에 한 줄 넣고 GitHub 에 올린다)
        static partial void ShowAddAppImpl(MainForm f);
        public static void ShowAddApp(MainForm f) { ShowAddAppImpl(f); }

        // 소스 올리기… (C:\dev 의 내 프로젝트를 비밀정보 검사 → 커밋·푸시)
        static partial void ShowUploadImpl(MainForm f);
        public static void ShowUpload(MainForm f) { ShowUploadImpl(f); }

        // 모두 최신으로 — 일하는 스레드에서 부른다(화면은 막지 않는다). 올린 수·실패 수를 돌려준다
        static partial void RunUpgradeImpl(ref int done, ref int fail);
        public static void RunUpgrade(ref int done, ref int fail) { RunUpgradeImpl(ref done, ref fail); }

        // 시작할 때 GitHub 에 새 판이 있으면 받아 바꾸고 다시 연다 — restarting=true 면 지금 프로그램은 바로 끝낸다
        static partial void TryUpdateImpl(string[] args, ref bool restarting);
        public static void TryUpdate(string[] args, ref bool restarting) { TryUpdateImpl(args, ref restarting); }
    }
}
