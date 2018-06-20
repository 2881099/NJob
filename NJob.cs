using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

/// <summary>
/// 作业调度器，一般运行在控制台
/// </summary>
public class NJob : IDisposable {

	private string _def_path;
	private List<NJobDef> _jobs;
	private Dictionary<string, NJobDef> _dic_jobs = new Dictionary<string, NJobDef>();
	private object _jobs_lock = new object();
	private FileSystemWatcher _defWatcher;
	public event NJobErrorHandler Error;
	public event NJobRunHandler Run;

	public NJob()
		: this(Path.Combine(AppContext.BaseDirectory, @"njob.txt")) {
	}
	public NJob(string path) {
		_def_path = path;
	}

	public void Start() {
		lock (_jobs_lock) {
			_dic_jobs.Clear();
			if (_jobs != null) {
				for (int a = 0; a < _jobs.Count; a++)
					_dic_jobs.Add(_jobs[a].Name, _jobs[a]);
				_jobs.Clear();
			}
		}
		if (!File.Exists(_def_path)) return;
		lock (_jobs_lock) {
			_jobs = LoadDef();
			foreach (NJobDef bot in _jobs)
				if (bot._timer == null) bot.RunNow();
		}
		if (_defWatcher == null) {
			_defWatcher = new FileSystemWatcher(Path.GetDirectoryName(_def_path), Path.GetFileName(_def_path));
			_defWatcher.Changed += delegate(object sender, FileSystemEventArgs e) {
				_defWatcher.EnableRaisingEvents = false;
				if (_jobs.Count > 0) {
					Start();
				}
				_defWatcher.EnableRaisingEvents = true;
			};
			_defWatcher.EnableRaisingEvents = true;
		}
	}
	public void Stop() {
		lock (_jobs_lock) {
			if (_jobs != null) {
				for (int a = 0; a < _jobs.Count; a++)
					_jobs[a].Dispose();
				_jobs.Clear();
			}
		}
	}

	#region IDisposable 成员

	public void Dispose() {
		if (_defWatcher != null)
			_defWatcher.Dispose();
		Stop();
	}

	#endregion

	public List<NJobDef> LoadDef() {
		string defDoc = Encoding.UTF8.GetString(readFile(_def_path));
		return LoadDef(defDoc);
	}
	public List<NJobDef> LoadDef(string defDoc) {
		Dictionary<string, NJobDef> dic = new Dictionary<string, NJobDef>();
		string[] defs = defDoc.Split(new string[] { "\n" }, StringSplitOptions.None);
		int row = 1;
		foreach (string def in defs) {
			string loc1 = def.Trim().Trim('\r');
			if (string.IsNullOrEmpty(loc1) || loc1[0] == 65279 || loc1[0] == ';' || loc1[0] == '#') continue;
			string pattern = @"([^\s]+)\s+(NONE|SEC|MIN|HOUR|DAY|RunOnDay|RunOnWeek|RunOnMonth)\s+([^\s]+)\s+([^\s]+)";
			Match m = Regex.Match(loc1, pattern, RegexOptions.IgnoreCase);
			if (!m.Success) {
				onError(new Exception("NJob配置错误“" + loc1 + "”, 第" + row + "行"));
				continue;
			}
			string name = m.Groups[1].Value.Trim('\t', ' ');
			NJobRunMode mode = getMode(m.Groups[2].Value.Trim('\t', ' '));
			string param = m.Groups[3].Value.Trim('\t', ' ');
			string runParam = m.Groups[4].Value.Trim('\t', ' ');
			if (dic.ContainsKey(name)) {
				onError(new Exception("NJob配置存在重复的名字“" + name + "”, 第" + row + "行"));
				continue;
			}
			if (mode == NJobRunMode.NONE) continue;

			NJobDef rd = null;
			if (_dic_jobs.ContainsKey(name)) {
				rd = _dic_jobs[name];
				rd.Update(mode, param, runParam);
				_dic_jobs.Remove(name);
			} else rd = new NJobDef(this, name, mode, param, runParam);
			if (rd.Interval < 0) {
				onError(new Exception("NJob配置参数错误“" + def + "”, 第" + row + "行"));
				continue;
			}
			dic.Add(rd.Name, rd);
			row++;
		}
		List<NJobDef> rds = new List<NJobDef>();
		foreach (NJobDef rd in dic.Values)
			rds.Add(rd);
		foreach (NJobDef stopBot in _dic_jobs.Values)
			stopBot.Dispose();

		return rds;
	}

	private void onError(Exception ex) {
		onError(ex, null);
	}
	internal void onError(Exception ex, NJobDef def) {
		if (Error != null)
			Error(this, new NJobErrorEventArgs(ex, def));
	}
	internal void onRun(NJobDef def) {
		if (Run != null)
			Run(this, def);
	}
	private byte[] readFile(string path) {
		if (File.Exists(path)) {
			string destFileName = Path.GetTempFileName();
			File.Copy(path, destFileName, true);
			int read = 0;
			byte[] data = new byte[1024];
			using (MemoryStream ms = new MemoryStream()) {
				using (FileStream fs = new FileStream(destFileName, FileMode.OpenOrCreate, FileAccess.Read)) {
					do {
						read = fs.Read(data, 0, data.Length);
						if (read <= 0) break;
						ms.Write(data, 0, read);
					} while (true);
				}
				File.Delete(destFileName);
				data = ms.ToArray();
			}
			return data;
		}
		return new byte[] { };
	}
	private NJobRunMode getMode(string mode) {
		mode = string.Concat(mode).ToUpper();
		switch (mode) {
			case "SEC": return NJobRunMode.SEC;
			case "MIN": return NJobRunMode.MIN;
			case "HOUR": return NJobRunMode.HOUR;
			case "DAY": return NJobRunMode.DAY;
			case "RUNONDAY": return NJobRunMode.RunOnDay;
			case "RUNONWEEK": return NJobRunMode.RunOnWeek;
			case "RUNONMONTH": return NJobRunMode.RunOnMonth;
			default: return NJobRunMode.NONE;
		}
	}
}

public class NJobDef : IDisposable {
	private string _name;
	private NJobRunMode _mode = NJobRunMode.NONE;
	private string _param;
	private string _runParam;
	private int _runTimes = 0;
	private int _errTimes = 0;

	private NJob _onwer;
	internal Timer _timer;
	private bool _timerIntervalOverflow = false;

	public NJobDef(NJob onwer, string name, NJobRunMode mode, string param, string runParam) {
		_onwer = onwer;
		_name = name;
		_mode = mode;
		_param = param;
		_runParam = runParam;
	}
	public void Update(NJobRunMode mode, string param, string runParam) {
		if (_mode != mode || _param != param || _runParam != runParam) {
			_mode = mode;
			_param = param;
			_runParam = runParam;
			if (_timer != null) {
				_timer.Dispose();
				_timer = null;
			}
			RunNow();
		}
	}

	public void RunNow() {
		double tmp = this.Interval;
		_timerIntervalOverflow = tmp > int.MaxValue;
		int interval = _timerIntervalOverflow ? int.MaxValue : (int)tmp;
		if (interval <= 0) {
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine("		{0} Interval <= 0", _name);
			Console.ResetColor();
			return;
		}
		//Console.WriteLine(interval);
		if (_timer == null) {
			_timer = new Timer(a => {
				if (_timerIntervalOverflow) {
					RunNow();
					return;
				}
				_runTimes++;
				string logObj = this.ToString();
				try {
					_onwer.onRun(this);
				} catch (Exception ex) {
					_errTimes++;
					_onwer.onError(ex, this);
				}
				RunNow();
			}, null, interval, -1);
		} else {
			_timer.Change(interval, -1);
		}
		if (tmp > 1000 * 9) {
			DateTime nextTime = DateTime.Now.AddMilliseconds(tmp);
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine("		{0} 下次触发时间：{1:yyyy-MM-dd HH:mm:ss}", _name, nextTime);
			Console.ResetColor();
		}
	}

	public override string ToString() {
		return Name + ", " + Mode + ", " + Param + ", " + RunParam;
	}

	#region IDisposable 成员

	public void Dispose() {
		if (_timer != null) {
			_timer.Dispose();
			_timer = null;
		}
	}

	#endregion

	public string Name { get { return _name; } }
	public NJobRunMode Mode { get { return _mode; } }
	public string Param { get { return _param; } }
	public string RunParam { get { return _runParam; } }
	public int RunTimes { get { return _runTimes; } }
	public int ErrTimes { get { return _errTimes; } }

	public double Interval {
		get {
			DateTime now = DateTime.Now;
			DateTime curt = DateTime.MinValue;
			TimeSpan ts = TimeSpan.Zero;
			uint ww = 0, dd = 0, hh = 0, mm = 0, ss = 0;
			double interval = -1;
			switch (_mode) {
				case NJobRunMode.SEC:
					double.TryParse(_param, out interval);
					interval *= 1000;
					break;
				case NJobRunMode.MIN:
					double.TryParse(_param, out interval);
					interval *= 60 * 1000;
					break;
				case NJobRunMode.HOUR:
					double.TryParse(_param, out interval);
					interval *= 60 * 60 * 1000;
					break;
				case NJobRunMode.DAY:
					double.TryParse(_param, out interval);
					interval *= 24 * 60 * 60 * 1000;
					break;
				case NJobRunMode.RunOnDay:
					List<string> hhmmss = new List<string>(string.Concat(_param).Split(':'));
					if (hhmmss.Count == 3)
						if (uint.TryParse(hhmmss[0], out hh) && hh < 24 &&
							uint.TryParse(hhmmss[1], out mm) && mm < 60 &&
							uint.TryParse(hhmmss[2], out ss) && ss < 60) {
							curt = now.Date.AddHours(hh).AddMinutes(mm).AddSeconds(ss);
							ts = curt.Subtract(now);
							while (!(ts.TotalMilliseconds > 0)) {
								curt = curt.AddDays(1);
								ts = curt.Subtract(now);
							}
							interval = ts.TotalMilliseconds;
						}
					break;
				case NJobRunMode.RunOnWeek:
					string[] wwhhmmss = string.Concat(_param).Split(':');
					if (wwhhmmss.Length == 4)
						if (uint.TryParse(wwhhmmss[0], out ww) && ww < 7 &&
							uint.TryParse(wwhhmmss[1], out hh) && hh < 24 &&
							uint.TryParse(wwhhmmss[2], out mm) && mm < 60 &&
							uint.TryParse(wwhhmmss[3], out ss) && ss < 60) {
							curt = now.Date.AddHours(hh).AddMinutes(mm).AddSeconds(ss);
							ts = curt.Subtract(now);
							while(!(ts.TotalMilliseconds > 0 && (int)curt.DayOfWeek == ww)) {
								curt = curt.AddDays(1);
								ts = curt.Subtract(now);
							}
							interval = ts.TotalMilliseconds;
						}
					break;
				case NJobRunMode.RunOnMonth:
					string[] ddhhmmss = string.Concat(_param).Split(':');
					if (ddhhmmss.Length == 4)
						if (uint.TryParse(ddhhmmss[0], out dd) && dd > 0 && dd < 32 &&
							uint.TryParse(ddhhmmss[1], out hh) && hh < 24 &&
							uint.TryParse(ddhhmmss[2], out mm) && mm < 60 &&
							uint.TryParse(ddhhmmss[3], out ss) && ss < 60) {
							curt = new DateTime(now.Year, now.Month, (int)dd).AddHours(hh).AddMinutes(mm).AddSeconds(ss);
							ts = curt.Subtract(now);
							while (!(ts.TotalMilliseconds > 0)) {
								curt = curt.AddMonths(1);
								ts = curt.Subtract(now);
							}
							interval = ts.TotalMilliseconds;
						}
					break;
			}
			if (interval == 0) interval = 1;
			return interval;
		}
	}
}
/*
; 和 # 匀为行注释
;SEC：					按秒触发
;MIN：					按分触发
;HOUR：					按时触发
;DAY：					按天触发
;RunOnDay：				每天 什么时间 触发
;RunOnWeek：			星期几 什么时间 触发
;RunOnMonth：			每月 第几天 什么时间 触发

;Name1		SEC			2				/schedule/test002.aspx
;Name2		MIN			2				/schedule/test002.aspx
;Name3		HOUR		1				/schedule/test002.aspx
;Name4		DAY			2				/schedule/test002.aspx
;Name5		RunOnDay	15:55:59		/schedule/test002.aspx
;每天15点55分59秒
;Name6		RunOnWeek	1:15:55:59		/schedule/test002.aspx
;每星期一15点55分59秒
;Name7		RunOnMonth	1:15:55:59		/schedule/test002.aspx
;每月1号15点55分59秒
*/
public enum NJobRunMode {
	NONE = 0,
	SEC = 1,
	MIN = 2,
	HOUR = 3,
	DAY = 4,
	RunOnDay = 11,
	RunOnWeek = 12,
	RunOnMonth = 13
}

public delegate void NJobErrorHandler(object sender, NJobErrorEventArgs e);
public delegate void NJobRunHandler(object sender, NJobDef e);
public class NJobErrorEventArgs : EventArgs {

	private Exception _exception;
	private NJobDef _def;

	public NJobErrorEventArgs(Exception exception, NJobDef def) {
		_exception = exception;
		_def = def;
	}

	public Exception Exception {
		get { return _exception; }
	}
	public NJobDef Def {
		get { return _def; }
	}
}