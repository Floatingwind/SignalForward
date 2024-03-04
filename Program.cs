namespace SignalForward
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main()
        {
            log4net.Config.XmlConfigurator.Configure();
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
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new SignalForwardTcp());
                mutex.ReleaseMutex();
            }
            else
            {
                MessageBox.Show(null, "��һ���ͱ�������ͬ��Ӧ�ó����Ѿ������У��벻Ҫͬʱ���ж��������\n\n������򼴽��˳���", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Application.Exit();//�˳�����
            }
        }
    }
}