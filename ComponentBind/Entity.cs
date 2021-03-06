﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using System.Collections;
using System.ComponentModel;
using System.Reflection;
using Newtonsoft.Json;

namespace ComponentBind
{
	[XmlInclude(typeof(Entity.Handle))]
	[XmlInclude(typeof(Property<Entity.Handle>))]
	[XmlInclude(typeof(ListProperty<Entity.Handle>))]
	[XmlInclude(typeof(ListProperty<string>))]
	[XmlInclude(typeof(Transform))]
	public class Entity
	{
		public class CommandLink
		{
			public Handle TargetEntity;

			[XmlAttribute]
			[DefaultValue("")]
			public string TargetCommand;

			[XmlAttribute]
			[DefaultValue("")]
			public string SourceCommand;

			[XmlIgnore]
			[JsonIgnore]
			public Command LinkedTargetCmd;

			[XmlIgnore]
			[JsonIgnore]
			public Command LinkedSourceCmd;
		}

		public struct Handle
		{
			private ulong guid;

			[XmlAttribute]
			[DefaultValue(0)]
			public ulong GUID;

			private Entity target;

			[XmlIgnore]
			[JsonIgnore]
			public Entity Target
			{
				get
				{
					if (this.target == null || this.target.GUID != this.GUID)
						Entity.guidTable.TryGetValue(this.GUID, out this.target);
					return this.target;
				}
				set
				{
					this.target = value;
					this.GUID = this.target == null ? 0 : this.target.GUID;
				}
			}

			public static implicit operator Entity(Handle obj)
			{
				return obj.Target;
			}

			public static implicit operator Handle(Entity obj)
			{
				return new Handle { Target = obj, GUID = obj == null ? 0 : obj.GUID };
			}

			public override bool Equals(object obj)
			{
				if (obj is Handle)
					return ((Handle)obj).GUID == this.GUID;
				else if (obj is Entity)
					return ((Entity)obj).GUID == this.GUID;
				else
					return false;
			}

			public override int GetHashCode()
			{
				return (int)(this.GUID & 0xffffffff);
			}
		}

		private static Dictionary<ulong, Entity> guidTable = new Dictionary<ulong, Entity>();
		private static Dictionary<string, Entity> idTable = new Dictionary<string, Entity>();

		private static List<IComponent> componentCache = new List<IComponent>();

		public static Entity GetByID(string id)
		{
			Entity result;
			Entity.idTable.TryGetValue(id, out result);
			return result;
		}

		public static Entity GetByGUID(ulong id)
		{
			Entity result;
			Entity.guidTable.TryGetValue(id, out result);
			return result;
		}

		[XmlIgnore]
		[JsonIgnore]
		public bool Active = true;

		[XmlAttribute]
		public string Type;

		[XmlIgnore]
		[JsonIgnore]
		public bool EditorCanDelete = true;

		public Property<string> ID = new Property<string>();

		public override string ToString()
		{
			return string.Format("{0} [{1}]", string.IsNullOrEmpty(this.ID.Value) ? this.GUID.ToString() : this.ID.Value, this.Type);
		}

		[XmlIgnore]
		[JsonIgnore]
		public bool Serialize = true;

		[XmlIgnore]
		[JsonIgnore]
		public bool CannotSuspend;

		[XmlIgnore]
		[JsonIgnore]
		public bool CannotSuspendByDistance;

		[XmlIgnore]
		[JsonIgnore]
		public bool Added;

		private BaseMain main;
		private Dictionary<string, IComponent> components = new Dictionary<string, IComponent>();
		private Dictionary<Type, IComponent> componentsByType = new Dictionary<Type, IComponent>();
		private List<IBinding> bindings = new List<IBinding>();

		public static ulong CurrentGUID = 1;

		[XmlAttribute]
		public ulong GUID;

		private Dictionary<string, Command.Entry> commands = new Dictionary<string, Command.Entry>();

		private readonly Dictionary<string, PropertyEntry> properties = new Dictionary<string, PropertyEntry>();

		[XmlIgnore]
		[JsonIgnore]
		public Command Delete = new Command();

		[XmlArray("LinkedCommands")]
		[XmlArrayItem("CommandLink", typeof(CommandLink))]
		[JsonProperty]
		public ListProperty<CommandLink> LinkedCommands = new ListProperty<CommandLink>();

		[XmlArray("Components")]
		[XmlArrayItem("Component", Type = typeof(DictionaryEntry))]
		[JsonProperty]
		public DictionaryEntry[] Components
		{
			get
			{
				// Make an array of DictionaryEntries to return
				IEnumerable<KeyValuePair<string, IComponent>> serializableComponents = this.components.Where(x => x.Value.Serialize);
				DictionaryEntry[] ret = new DictionaryEntry[serializableComponents.Count()];
				int i = 0;
				DictionaryEntry de;
				// Iterate through properties to load items into the array.
				foreach (KeyValuePair<string, IComponent> component in serializableComponents)
				{
					de = new DictionaryEntry();
					de.Key = component.Key;
					de.Value = component.Value;
					component.Value.OnSave();
					ret[i] = de;
					i++;
				}
				if (this.OnSave != null)
					this.OnSave.Execute();
				return ret;
			}
			set
			{
				this.components.Clear();
				for (int i = 0; i < value.Length; i++)
				{
					IComponent c = value[i].Value as IComponent;
					if (c != null)
					{
						this.components.Add((string)value[i].Key, c);
						Type t = c.GetType();
						do
						{
							this.componentsByType[t] = c;
							t = t.BaseType;
						}
						while (t.Assembly != Entity.componentBindAssembly);
					}
				}
			}
		}

		[XmlIgnore]
		[JsonIgnore]
		public Dictionary<string, IComponent> ComponentDictionary
		{
			get
			{
				return this.components;
			}
		}

		[XmlIgnore]
		[JsonIgnore]
		public IEnumerable<KeyValuePair<string, Command.Entry>> Commands
		{
			get
			{
				return this.commands;
			}
		}

		[XmlIgnore]
		[JsonIgnore]
		public IEnumerable<KeyValuePair<string, PropertyEntry>> Properties
		{
			get
			{
				return this.properties;
			}
		}

		public Entity()
		{
			// Called by XmlSerializer
			this.Delete.Action = (Action)this.delete;
		}

		[XmlIgnore]
		[JsonIgnore]
		public Command OnSave;

		[XmlIgnore]
		[JsonIgnore]
		public Property<bool> EditorSelected;

		private static Assembly componentBindAssembly;

		static Entity()
		{
			Entity.componentBindAssembly = Assembly.GetExecutingAssembly();
		}

		public Entity(BaseMain _main, string _type)
			: this()
		{
			// Called by a Factory
			this.Type = _type;
		}

		public void ClearGUID()
		{
			if (this.GUID != 0)
				Entity.guidTable.Remove(this.GUID);
		}

		public void NewGUID()
		{
			this.ClearGUID();
			this.GUID = Entity.CurrentGUID;
			Entity.CurrentGUID++;
			Entity.guidTable.Add(this.GUID, this);
		}

		public void SetMain(BaseMain _main)
		{
			if (this.GUID == 0)
				this.GUID = Entity.CurrentGUID;

			Entity.CurrentGUID = Math.Max(Entity.CurrentGUID, this.GUID + 1);
			Entity.guidTable.Add(this.GUID, this);

			this.main = _main;

			if (!string.IsNullOrEmpty(this.ID))
				Entity.idTable.Add(this.ID, this);

			if (_main.EditorEnabled)
			{
				this.OnSave = new Command();
				this.EditorSelected = new Property<bool>();
				string oldId = this.ID;
				this.Add(new NotifyBinding(delegate()
				{
					if (!string.IsNullOrEmpty(oldId))
						Entity.idTable.Remove(oldId);
					if (!string.IsNullOrEmpty(this.ID))
						Entity.idTable.Add(this.ID, this);
					oldId = this.ID;
				}, this.ID));
			}

			Entity.componentCache.AddRange(this.components.Values);
			for (int i = 0; i < Entity.componentCache.Count; i++)
			{
				IComponent c = Entity.componentCache[i];
				c.Entity = this;
				this.main.AddComponent(c);
			}
			Entity.componentCache.Clear();
		}

		public void SetSuspended(bool suspended)
		{
			Entity.componentCache.AddRange(this.components.Values);
			for (int i = 0; i < Entity.componentCache.Count; i++)
			{
				IComponent c = Entity.componentCache[i];
				if (c.Suspended.Value != suspended)
					c.Suspended.Value = suspended;
			}
			Entity.componentCache.Clear();
		}

		public void LinkedCommandCall(CommandLink link)
		{
			if (link.LinkedTargetCmd != null)
				link.LinkedTargetCmd.Execute();
			else if (link.TargetEntity.Target != null)
			{
				Command destCommand = link.TargetEntity.Target.getCommand(link.TargetCommand);
				if (destCommand != null)
				{
					link.LinkedTargetCmd = destCommand;
					destCommand.Execute();
				}
			}
		}

		public void Add(string name, Command cmd, Command.Perms perms = Command.Perms.Linkable, string description = null)
		{
			Command.Entry entry = new Command.Entry { Command = cmd, Permissions = perms, Key = name };
			if (this.main.EditorEnabled)
				entry.Description = description;
			this.commands.Add(name, entry);
			for (int i = 0; i < this.LinkedCommands.Count; i++)
			{
				CommandLink link = this.LinkedCommands[i];
				if (link.LinkedSourceCmd == null && name == link.SourceCommand)
				{
					link.LinkedSourceCmd = cmd;
					this.Add(new CommandBinding(link.LinkedSourceCmd, () => LinkedCommandCall(link)));
				}
			}
		}

		public void Add(string name, IComponent component)
		{
			this.components.Add(name, component);
			Type t = component.GetType();
			do
			{
				this.componentsByType[t] = component;
				t = t.BaseType;
			}
			while (t.Assembly != Entity.componentBindAssembly);
			if (this.main != null)
			{
				component.Entity = this;
				this.main.AddComponent(component);
			}
		}

		public void AddWithoutOverwriting(string name, IComponent component)
		{
			this.components.Add(name, component);
			Type t = component.GetType();
			do
			{
				if (!this.componentsByType.ContainsKey(t))
					this.componentsByType[t] = component;
				t = t.BaseType;
			}
			while (t.Assembly != Entity.componentBindAssembly);
			if (this.main != null)
			{
				component.Entity = this;
				this.main.AddComponent(component);
			}
		}

		public void Add(IComponent component)
		{
			component.Serialize = false;
			this.Add(Guid.NewGuid().ToString(), component);
		}

		public void Add(string name, IProperty prop, PropertyEntry.EditorData editorData)
		{
			this.properties.Add(name, new PropertyEntry(prop, this.main.EditorEnabled ? editorData : null));
		}

		public void Add(string name, IProperty prop, string description = null, bool readOnly = false)
		{
			PropertyEntry.EditorData data = null;
			if (this.main.EditorEnabled)
			{
				data = new PropertyEntry.EditorData();
				data.Readonly = readOnly;
				data.Description = description;
			}
			this.properties.Add(name, new PropertyEntry(prop, data));
		}

		public void RemoveProperty(string name)
		{
			try
			{
				this.properties.Remove(name);
			}
			catch (KeyNotFoundException)
			{

			}
		}

		public void RemoveCommand(string name)
		{
			try
			{
				this.commands.Remove(name);
			}
			catch (KeyNotFoundException)
			{

			}

			for (int i = this.LinkedCommands.Count - 1; i >= 0; i--)
			{
				CommandLink link = this.LinkedCommands[i];
				if (link.SourceCommand == name)
					this.LinkedCommands.RemoveAt(i);
			}
		}

		public Property<T> GetProperty<T>(string name)
		{
			if (name == null) return null;
			PropertyEntry result;
			this.properties.TryGetValue(name, out result);
			if (result == null)
				return null;
			else
				return (Property<T>)result.Property;
		}

		public IProperty GetProperty(string name)
		{
			if (name == null) return null;
			PropertyEntry result;
			this.properties.TryGetValue(name, out result);
			if (result == null)
				return null;
			else
				return result.Property;
		}

		public void AddWithoutOverwriting(IComponent component)
		{
			this.AddWithoutOverwriting(Guid.NewGuid().ToString(), component);
		}

		public void Add(IBinding binding)
		{
			this.bindings.Add(binding);
		}

		public void RemoveComponent(string name)
		{
			IComponent c;
			this.components.TryGetValue(name, out c);
			if (c != null)
			{
				this.components.Remove(name);
				this.removeComponentTypeMapping(c);
			}
		}

		public void Remove(IBinding b)
		{
			b.Delete();
			this.bindings.Remove(b);
		}

		public void Remove(IComponent c)
		{
			foreach (KeyValuePair<string, IComponent> pair in this.components)
			{
				if (pair.Value == c)
				{
					this.components.Remove(pair.Key);
					break;
				}
			}

			this.removeComponentTypeMapping(c);
		}

		private void removeComponentTypeMapping(IComponent c)
		{
			Type type = c.GetType();
			do
			{
				IComponent typeComponent = null;
				this.componentsByType.TryGetValue(type, out typeComponent);
				if (typeComponent == c)
				{
					bool foundReplacement = false;
					foreach (IComponent c2 in this.components.Values)
					{
						if (c2.GetType().Equals(type))
						{
							this.componentsByType[type] = c2;
							foundReplacement = true;
							break;
						}
					}
					if (!foundReplacement)
						this.componentsByType.Remove(type);
				}
				type = type.BaseType;
			}
			while (type.Assembly != Entity.componentBindAssembly);
		}

		public T Get<T>() where T : IComponent
		{
			IComponent result = null;
			this.componentsByType.TryGetValue(typeof(T), out result);
			return (T)result;
		}

		public T GetOrCreate<T>() where T : IComponent, new()
		{
			IComponent result = null;
			this.componentsByType.TryGetValue(typeof(T), out result);
			if (result == null)
			{
				result = new T();
				this.Add(result);
			}
			return (T)result;
		}

		public IEnumerable<T> GetAll<T>() where T : IComponent
		{
			return this.components.Values.OfType<T>();
		}

		public T Get<T>(string name) where T : IComponent
		{
			IComponent result = null;
			this.components.TryGetValue(name, out result);
			return (T)result;
		}

		public T GetOrCreate<T>(string name) where T : IComponent, new()
		{
			IComponent result = null;
			this.components.TryGetValue(name, out result);
			if (result == null)
			{
				result = new T();
				this.Add(name, result);
			}
			return (T)result;
		}

		public T GetOrCreateWithoutOverwriting<T>(string name) where T : IComponent, new()
		{
			IComponent result = null;
			this.components.TryGetValue(name, out result);
			if (result == null)
			{
				result = new T();
				this.AddWithoutOverwriting(name, result);
			}
			return (T)result;
		}

		public T Create<T>(string name = null) where T : IComponent, new()
		{
			T result = new T();
			if (name == null)
				this.Add(result);
			else
				this.Add(name, result);
			return (T)result;
		}

		public T GetOrCreate<T>(string name, out bool created) where T : IComponent, new()
		{
			IComponent result = null;
			this.components.TryGetValue(name, out result);
			created = false;
			if (result == null)
			{
				created = true;
				result = new T();
				this.Add(name, result);
			}
			return (T)result;
		}

		private Command getCommand(string name)
		{
			Command.Entry result;
			if (this.commands.TryGetValue(name, out result))
				return result.Command;
			else
				return null;
		}

		protected void delete()
		{
			if (this.Active)
			{
				this.Active = false;
				Entity.componentCache.AddRange(this.components.Values);
				this.components.Clear();
				this.componentsByType.Clear();
				for (int i = 0; i < Entity.componentCache.Count; i++)
					Entity.componentCache[i].Delete.Execute();
				Entity.componentCache.Clear();
				for (int i = 0; i < this.bindings.Count; i++)
					this.bindings[i].Delete();
				this.bindings.Clear();
				this.commands.Clear();
				this.main.Remove(this);
				this.ClearGUID();
				if (!string.IsNullOrEmpty(this.ID))
					Entity.idTable.Remove(this.ID);
			}
		}
	}
}
