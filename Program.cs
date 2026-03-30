namespace SignalForward
{
    internal static class Program
    {
        private static readonly log4net.ILog Logger = log4net.LogManager.GetLogger(typeof(Program));

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main()
        {
            log4net.Config.XmlConfigurator.Configure();

            // 捕获UI线程未处理异常
            Application.ThreadException += Application_ThreadException;
            // 设置UI线程异常处理模式
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            // 捕获非UI线程未处理异常
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            // 捕获Task中未观察到的异常
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            //ApplicationConfiguration.Initialize();
            //Application.EnableVisualStyles();
            //Application.SetCompatibleTextRenderingDefault(false);
            //Application.Run(new Form1());
            bool ret;
            System.Threading.Mutex mutex = new System.Threading.Mutex(true, Application.ProductName, out ret);
            if (ret)
            {
                //ApplicationConfiguration.Initialize();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new SignalForwardUdp());
                mutex.ReleaseMutex();
            }
            else
            {
                MessageBox.Show(null, "有一个和本程序相同的应用程序已经在运行，请不要同时运行多个本程序。\n\n这个程序即将退出。", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Application.Exit();//退出程序
            }
        }

        /// <summary>
        /// UI线程未处理异常
        /// </summary>
        private static void Application_ThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            Logger.Fatal("UI线程未处理异常", e.Exception);
        }

        /// <summary>
        /// 非UI线程未处理异常
        /// </summary>
        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                Logger.Fatal("非UI线程未处理异常，IsTerminating=" + e.IsTerminating, ex);
            }
            else
            {
                Logger.Fatal("非UI线程未处理异常，IsTerminating=" + e.IsTerminating + "，ExceptionObject=" + e.ExceptionObject);
            }
        }

        /// <summary>
        /// Task未观察到的异常
        /// </summary>
        private static void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Logger.Error("Task未观察到的异常", e.Exception);
            e.SetObserved();
        }
    }
}