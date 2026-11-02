using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace RestraintHub
{
    /// <summary>
    /// Custom Missions v2's script engine (GUID <c>SFM_custom_mission_v2</c>), bound by reflection so the hub loads without it and
    /// a changed version fails with one clear log line instead of a crash. Members from 2.4.0 (<c>dev ref extract --interop
    /// game/BepInEx/plugins --only SFM_custom_mission_v2</c>): <c>Script.Engine.RegisterFunction(string, Func&lt;ProgramVariables,
    /// ProgramThreadBase, ProgramValue&gt;)</c>, <c>Engine.SetEvent(string, ProgramValue)</c>, <c>Engine.programs</c>;
    /// <c>ProgramValue</c> (constructors from bool / double / string / ProgramValueList, <c>Null</c>, <c>IsNull</c>,
    /// <c>ToFloat</c>, <c>ToBool</c>, <c>ToString</c>, <c>ToList</c>, <c>GetTypeString</c>); <c>ProgramValueList(int)</c>
    /// (<c>Insert(ProgramValue, int)</c>, <c>Count()</c>, indexers); <c>ProgramVariables</c> (<c>HasVariable</c>,
    /// <c>GetVariable(string, bool)</c>; unnamed arguments in the list <c>_args</c>); <c>ProgramThreadBase.program</c>
    /// (<c>Program.name</c>, <c>GetActive()</c>) and <c>destroyed</c>.
    /// </summary>
    internal sealed class Cm2
    {
        private Type _value, _list, _vars, _thread;
        private MethodInfo _register, _setEvent, _hasVar, _getVar, _isNull, _toFloat, _toBool, _toList, _typeString, _insert, _count, _getActive, _setActive;
        private PropertyInfo _listItemInt, _listItemStr, _destroyed;
        private FieldInfo _null, _programs, _threadProgram, _programName;
        private ConstructorInfo _fromBool, _fromDouble, _fromString, _fromList, _newList;

        /// <summary>Bind to the engine in <paramref name="asm"/>; null and <paramref name="problem"/> when a member is missing.</summary>
        public static Cm2 Bind(Assembly asm, out string problem)
        {
            problem = null;
            try
            {
                var c = new Cm2();
                Type T(string name) => asm.GetType("SFM_custom_mission_v2.Script." + name, true);
                Type engine = T("Engine");
                c._value = T("ProgramValue");
                c._list = T("ProgramValueList");
                c._vars = T("ProgramVariables");
                c._thread = T("ProgramThreadBase");
                Type program = T("Program");
                var fn = typeof(Func<,,>).MakeGenericType(c._vars, c._thread, c._value);
                c._register = Need(engine.GetMethod("RegisterFunction", new[] { typeof(string), fn }), "Engine.RegisterFunction");
                c._setEvent = Need(engine.GetMethod("SetEvent", new[] { typeof(string), c._value }), "Engine.SetEvent");
                c._programs = Need(engine.GetField("programs", BindingFlags.Public | BindingFlags.Static), "Engine.programs");
                c._hasVar = Need(c._vars.GetMethod("HasVariable", new[] { typeof(string) }), "ProgramVariables.HasVariable");
                c._getVar = Need(c._vars.GetMethod("GetVariable", new[] { typeof(string), typeof(bool) }), "ProgramVariables.GetVariable");
                c._isNull = Need(c._value.GetMethod("IsNull", Type.EmptyTypes), "ProgramValue.IsNull");
                c._toFloat = Need(c._value.GetMethod("ToFloat", Type.EmptyTypes), "ProgramValue.ToFloat");
                c._toBool = Need(c._value.GetMethod("ToBool", Type.EmptyTypes), "ProgramValue.ToBool");
                c._toList = Need(c._value.GetMethod("ToList", Type.EmptyTypes), "ProgramValue.ToList");
                c._typeString = Need(c._value.GetMethod("GetTypeString", Type.EmptyTypes), "ProgramValue.GetTypeString");
                c._null = Need(c._value.GetField("Null", BindingFlags.Public | BindingFlags.Static), "ProgramValue.Null");
                c._fromBool = Need(c._value.GetConstructor(new[] { typeof(bool) }), "ProgramValue(bool)");
                c._fromDouble = Need(c._value.GetConstructor(new[] { typeof(double) }), "ProgramValue(double)");
                c._fromString = Need(c._value.GetConstructor(new[] { typeof(string) }), "ProgramValue(string)");
                c._fromList = Need(c._value.GetConstructor(new[] { c._list }), "ProgramValue(ProgramValueList)");
                c._newList = Need(c._list.GetConstructor(new[] { typeof(int) }), "ProgramValueList(int)");
                c._insert = Need(c._list.GetMethod("Insert", new[] { c._value, typeof(int) }), "ProgramValueList.Insert");
                c._count = Need(c._list.GetMethod("Count", Type.EmptyTypes), "ProgramValueList.Count");
                c._listItemInt = Need(c._list.GetProperty("Item", c._value, new[] { typeof(int) }), "ProgramValueList[int]");
                c._listItemStr = Need(c._list.GetProperty("Item", c._value, new[] { typeof(string) }), "ProgramValueList[string]");
                c._threadProgram = Need(c._thread.GetField("program"), "ProgramThreadBase.program");
                c._destroyed = Need(c._thread.GetProperty("destroyed"), "ProgramReferencedObject.destroyed");
                c._programName = Need(program.GetField("name"), "Program.name");
                c._getActive = Need(program.GetMethod("GetActive", Type.EmptyTypes), "Program.GetActive");
                c._setActive = program.GetMethod("SetActive", new[] { typeof(bool) }); // diagnostics only
                return c;
            }
            catch (Exception e)
            {
                problem = e is MissingMemberException ? e.Message : e.GetType().Name + ": " + e.Message;
                return null;
            }
        }

        private static T Need<T>(T member, string name) where T : class =>
            member ?? throw new MissingMemberException("Custom Missions v2 has no " + name);

        /// <summary>Make <paramref name="name"/> a script function: <paramref name="handler"/> gets the arguments and the calling
        /// thread (both opaque) and returns a value made here (null = the script's null).</summary>
        public void Register(string name, Func<object, object, object> handler)
        {
            var vars = Expression.Parameter(_vars, "parameters");
            var thread = Expression.Parameter(_thread, "thread");
            var call = Expression.Invoke(Expression.Constant(handler),
                Expression.Convert(vars, typeof(object)), Expression.Convert(thread, typeof(object)));
            var body = Expression.Convert(Expression.Coalesce(call, Expression.Constant(_null.GetValue(null), typeof(object))), _value);
            var fn = Expression.Lambda(typeof(Func<,,>).MakeGenericType(_vars, _thread, _value), body, vars, thread).Compile();
            _register.Invoke(null, new object[] { name, fn });
        }

        // --- arguments ----------------------------------------------------------------------------------------------------------

        /// <summary>The argument named <paramref name="name"/>, else unnamed argument number <paramref name="index"/> (-1: named
        /// only); null when not given or the script's null.</summary>
        public object Arg(object vars, string name, int index)
        {
            object v = null;
            if (name != null && (bool)_hasVar.Invoke(vars, new object[] { name })) v = _getVar.Invoke(vars, new object[] { name, false });
            else if (index >= 0 && (bool)_hasVar.Invoke(vars, new object[] { "_args" }))
            {
                var args = _getVar.Invoke(vars, new object[] { "_args", false });
                var list = args == null || IsNull(args) ? null : _toList.Invoke(args, null);
                if (list != null && index < (int)_count.Invoke(list, null)) v = _listItemInt.GetValue(list, new object[] { index });
            }
            return v == null || IsNull(v) ? null : v;
        }

        public bool IsNull(object v) => v == null || (bool)_isNull.Invoke(v, null);
        public string Text(object v) => v == null ? null : v.ToString();
        public string TypeOf(object v) => v == null ? "null" : (string)_typeString.Invoke(v, null);
        public double Number(object v) => (double)_toFloat.Invoke(v, null);
        public bool Truth(object v) => (bool)_toBool.Invoke(v, null);

        // --- values -------------------------------------------------------------------------------------------------------------

        public object Null => _null.GetValue(null);
        public object Of(bool b) => _fromBool.Invoke(new object[] { b });
        public object Of(double d) => _fromDouble.Invoke(new object[] { d });
        public object Of(string s) => s == null ? Null : _fromString.Invoke(new object[] { s });

        /// <summary>A list value with <paramref name="items"/> (values made here) at 0, 1, ...</summary>
        public object List(IEnumerable<object> items)
        {
            var list = _newList.Invoke(new object[] { 4 });
            int i = 0;
            foreach (var item in items) _insert.Invoke(list, new[] { item ?? Null, (object)i++ });
            return _fromList.Invoke(new[] { list });
        }

        /// <summary>A list value with named items (<c>data.id</c> in a script).</summary>
        public object Named(IEnumerable<KeyValuePair<string, object>> items)
        {
            var list = _newList.Invoke(new object[] { 4 });
            foreach (var kv in items) _listItemStr.SetValue(list, kv.Value ?? Null, new object[] { kv.Key });
            return _fromList.Invoke(new[] { list });
        }

        // --- engine -------------------------------------------------------------------------------------------------------------

        /// <summary>Set event <paramref name="name"/> for this frame: missions' <c>CreateEventListener</c>s on it run at once.</summary>
        public void SetEvent(string name, object value) => _setEvent.Invoke(null, new[] { name, value ?? Null });

        /// <summary>The mission (project) a script thread belongs to; null if unknown.</summary>
        public string ProgramOf(object thread)
        {
            if (thread == null) return null;
            var program = _threadProgram.GetValue(thread);
            return program == null ? null : _programName.GetValue(program) as string;
        }

        public bool Ended(object thread) => thread == null || (bool)_destroyed.GetValue(thread);

        /// <summary>A mission with this name is loaded and switched on.</summary>
        public bool Runs(string program)
        {
            if (!(_programs.GetValue(null) is IEnumerable all)) return false;
            foreach (var p in all)
                if (p != null && (_programName.GetValue(p) as string) == program) return (bool)_getActive.Invoke(p, null);
            return false;
        }

        /// <summary>Diagnostics: switch a mission on or off as the phone does; false if not found.</summary>
        public bool SetActive(string program, bool on)
        {
            if (_setActive == null || !(_programs.GetValue(null) is IEnumerable all)) return false;
            foreach (var p in all)
                if (p != null && (_programName.GetValue(p) as string) == program) { _setActive.Invoke(p, new object[] { on }); return true; }
            return false;
        }

        /// <summary>The loaded missions and whether each is on (diagnostics).</summary>
        public string Programs()
        {
            if (!(_programs.GetValue(null) is IEnumerable all)) return "none";
            var parts = new List<string>();
            foreach (var p in all)
                if (p != null) parts.Add(_programName.GetValue(p) + ((bool)_getActive.Invoke(p, null) ? "" : " (off)"));
            return parts.Count == 0 ? "none" : string.Join(", ", parts);
        }
    }
}
