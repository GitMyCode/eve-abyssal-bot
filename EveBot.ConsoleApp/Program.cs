using System;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using read_memory_64_bit;

namespace EveBot.ConsoleApp
{
    class Program
    {
        static void Main(string[] args)
        {
            var app = new Program();

            var process = app.GetEveProcess();
            Console.WriteLine(process?.Id.ToString() ?? "not found");

            var uiRootAddress = app.FindUIRootAddressFromProcessId(process.Id);
            Console.WriteLine(uiRootAddress.HasValue ? uiRootAddress.Value.ToString() : "not found ui root address");

            string memoryReadingSerialRepresentationJson = null;
            using (var memoryReader = new read_memory_64_bit.MemoryReaderFromLiveProcess(process.Id))
            {
                var uiTree = read_memory_64_bit.EveOnline64.ReadUITreeFromAddress(uiRootAddress.Value, memoryReader, 99);

                if (uiTree != null)
                {
                    memoryReadingSerialRepresentationJson = Newtonsoft.Json.JsonConvert.SerializeObject(
                        uiTree.WithOtherDictEntriesRemoved(),
                        //  Support popular JSON parsers: Wrap large integers in a string to work around limitations there. (https://discourse.elm-lang.org/t/how-to-parse-a-json-object/4977)
                        new read_memory_64_bit.IntegersToStringJsonConverter()
                        );
                }
            }
            
            System.IO.File.WriteAllText("./allo.json",memoryReadingSerialRepresentationJson);
        }

        void SetProcessDPIAware()
        {
            //  https://www.google.com/search?q=GetWindowRect+dpi
            //  https://github.com/dotnet/wpf/issues/859
            //  https://github.com/dotnet/winforms/issues/135
            WinApi.SetProcessDPIAware();
        }


        System.Diagnostics.Process GetEveProcess() =>
            System.Diagnostics.Process.GetProcessesByName("exefile").FirstOrDefault();


        ulong? FindUIRootAddressFromProcessId(int processId)
        {
            var candidatesAddresses =
                read_memory_64_bit.EveOnline64.EnumeratePossibleAddressesForUIRootObjectsFromProcessId(processId);

            using (var memoryReader = new read_memory_64_bit.MemoryReaderFromLiveProcess(processId))
            {
                var uiTrees =
                    candidatesAddresses
                    .Select(candidateAddress => read_memory_64_bit.EveOnline64.ReadUITreeFromAddress(candidateAddress, memoryReader, 99))
                    .ToList();

                return
                    uiTrees
                    .OrderByDescending(uiTree => uiTree?.EnumerateSelfAndDescendants().Count() ?? -1)
                    .FirstOrDefault()
                    ?.pythonObjectAddress;
            }
        }

    }

    static public class WinApi
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Point
        {
            public int x;
            public int y;

            public Point(int x, int y)
            {
                this.x = x;
                this.y = y;
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        static public extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        static public extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        /*
        https://stackoverflow.com/questions/19867402/how-can-i-use-enumwindows-to-find-windows-with-a-specific-caption-title/20276701#20276701
        https://stackoverflow.com/questions/295996/is-the-order-in-which-handles-are-returned-by-enumwindows-meaningful/296014#296014
        */
        public static System.Collections.Generic.IReadOnlyList<IntPtr> ListWindowHandlesInZOrder()
        {
            IntPtr found = IntPtr.Zero;
            System.Collections.Generic.List<IntPtr> windowHandles = new System.Collections.Generic.List<IntPtr>();

            EnumWindows(delegate (IntPtr wnd, IntPtr param)
            {
                windowHandles.Add(wnd);

                // return true here so that we iterate all windows
                return true;
            }, IntPtr.Zero);

            return windowHandles;
        }

        [DllImport("user32.dll")]
        static public extern IntPtr ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        static public extern IntPtr GetWindowRect(IntPtr hWnd, ref Rect rect);

        [DllImport("user32.dll", SetLastError = false)]
        static public extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static public extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        static public extern bool ClientToScreen(IntPtr hWnd, ref Point lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        static public extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }
}