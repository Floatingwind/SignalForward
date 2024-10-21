namespace SignalForward
{
    public partial class LoginScreen : Form
    {
        /// <summary>
        /// 密码
        /// </summary>
        public string Password { get; set; }

        public LoginScreen(string password)
        {
            InitializeComponent();
            Password = password;
        }

        /// <summary>
        /// 登录事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UserName_Click(object sender, EventArgs e)
        {
            if (textBox2.Text.Equals(Password))
            {
                DialogResult = DialogResult.OK;
            }
            else
            {
                MessageBox.Show("密码错误");
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.No;
        }
    }
}