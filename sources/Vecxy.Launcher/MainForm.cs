using Sunny.UI;

namespace Vecxy.Launcher;

public class MainForm : UIForm
{
    private UIButton _btnChangeColor;
    private UILabel _lblChangeColor;
    private Random _random;

    public MainForm()
    {
        _random = new Random();
        
        SetupForm();
    }

    private void SetupForm()
    {
        Text = "Vecxy.Launcher";
        Size = new Size(600, 400);
        StartPosition = FormStartPosition.CenterScreen;
        Style = UIStyle.LayuiGreen;
        Resizable = true;

        _btnChangeColor = new UIButton();
        _btnChangeColor.Text = "🎨 Сменить фон!";
        _btnChangeColor.Size = new Size(200, 60);
        _btnChangeColor.Style = UIStyle.Green;
        _btnChangeColor.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
        _btnChangeColor.Cursor = Cursors.Hand;

        _btnChangeColor.Location = new Point
        (
            (ClientSize.Width - _btnChangeColor.Width) / 2,
            (ClientSize.Height - _btnChangeColor.Height) / 2
        );

        _btnChangeColor.Click += BtnChangeColor_Click;

        _lblChangeColor = new UILabel();
        _lblChangeColor.Text = "test";
        _lblChangeColor.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
        _lblChangeColor.Style = UIStyle.Red;
        
        _lblChangeColor.Location = new Point
        (
            ((ClientSize.Width - _btnChangeColor.Width) / 2) + (_btnChangeColor.Width / 2 - _lblChangeColor.GetPreferredSize(Size.Empty).Width / 2),
            _btnChangeColor.Location.Y - _btnChangeColor.Height / 2
        );
        
        Controls.Add(_lblChangeColor);
        Controls.Add(_btnChangeColor);
    }

    private void BtnChangeColor_Click(object sender, EventArgs e)
    {
        var randomColor = Color.FromArgb
        (
            _random.Next(50, 256),
            _random.Next(50, 256), 
            _random.Next(50, 256)
        );

        BackColor = randomColor;
        
        _btnChangeColor.Style = (UIStyle)_random.Next(1, 14);
        _lblChangeColor.Text = $"{randomColor.R}, {randomColor.G}, {randomColor.B}";
        
        _lblChangeColor.Location = new Point
        (
            ((ClientSize.Width - _btnChangeColor.Width) / 2) + (_btnChangeColor.Width / 2 - _lblChangeColor.GetPreferredSize(Size.Empty).Width / 2),
            _btnChangeColor.Location.Y - _btnChangeColor.Height / 2
        );
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_btnChangeColor != null)
        {
            _btnChangeColor.Location = new Point(
                (ClientSize.Width - _btnChangeColor.Width) / 2,
                (ClientSize.Height - _btnChangeColor.Height) / 2
            );
            
            _lblChangeColor.Location = new Point
            (
                ((ClientSize.Width - _btnChangeColor.Width) / 2) + (_btnChangeColor.Width / 2 - _lblChangeColor.GetPreferredSize(Size.Empty).Width / 2),
                _btnChangeColor.Location.Y - _btnChangeColor.Height / 2
            );
        }
        
       
    }
}