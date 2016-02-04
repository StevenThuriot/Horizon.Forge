namespace Horizon.Forge
{
    public abstract class Member
    {
        public readonly string Name;

        protected Member(string name)
        {
            Name = name;
        }

        public static Member Field<T>(string name, T value)
        {
            return new FieldMember<T>(name, value);
        }

        public static Member Property<T>(string name, T value)
        {
            return new PropertyMember<T>(name, value);
        }

        public static Member Unknown<T>(string name, T value)
        {
            return new UnknownMember<T>(name, value);
        }

        internal abstract void SetValue(dynamic instance);
    }

    abstract class Member<T> : Member
    {
        public readonly T Value;
        
        protected Member(string name, T value)
            : base(name)
        {
            Value = value;
        }
    }
}
