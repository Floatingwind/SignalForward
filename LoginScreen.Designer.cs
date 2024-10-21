namespace SignalForward
{
    partial class LoginScreen
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.textBox2 = new System.Windows.Forms.TextBox();
            this.PA = new System.Windows.Forms.Label();
            this.UserName = new System.Windows.Forms.Button();
            this.button1 = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // textBox2
            // 
            this.textBox2.Location = new System.Drawing.Point(84, 132);
            this.textBox2.Name = "textBox2";
            this.textBox2.Size = new System.Drawing.Size(178, 21);
            this.textBox2.TabIndex = 3;
            // 
            // PA
            // 
            this.PA.AutoSize = true;
            this.PA.Location = new System.Drawing.Point(32, 136);
            this.PA.Name = "PA";
            this.PA.Size = new System.Drawing.Size(41, 12);
            this.PA.TabIndex = 2;
            this.PA.Text = "密码：";
            // 
            // UserName
            // 
            this.UserName.Location = new System.Drawing.Point(304, 131);
            this.UserName.Name = "UserName";
            this.UserName.Size = new System.Drawing.Size(75, 23);
            this.UserName.TabIndex = 4;
            this.UserName.Text = "登录";
            this.UserName.UseVisualStyleBackColor = true;
            this.UserName.Click += new System.EventHandler(this.UserName_Click);
            // 
            // button1
            // 
            this.button1.Location = new System.Drawing.Point(385, 131);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(75, 23);
            this.button1.TabIndex = 5;
            this.button1.Text = "退出";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // LoginScreen
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(487, 319);
            this.ControlBox = false;
            this.Controls.Add(this.button1);
            this.Controls.Add(this.UserName);
            this.Controls.Add(this.textBox2);
            this.Controls.Add(this.PA);
            this.Name = "LoginScreen";
            this.ShowInTaskbar = false;
            this.Text = "登录";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private TextBox textBox2;
        private Label PA;
        private Button UserName;
        private Button button1;
    }
}