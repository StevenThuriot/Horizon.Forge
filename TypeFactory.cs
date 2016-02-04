using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.Linq;

namespace Horizon.Forge
{
    public abstract class TypeFactory : DynamicObject
    {
        static IReadOnlyList<Member> CreateValueNodes(CallInfo callInfo, IReadOnlyList<object> args)
        {
            var result = new List<Member>();

            int difference;
            var nameCount = callInfo.ArgumentNames.Count;
            var argumentCount = args.Count;
            if (argumentCount != nameCount)
            {
                if (nameCount != args.Count(x => !(x is Member)))
                    throw new NotSupportedException("Unnamed arguments must be MemberDefinition instances.");

                difference = argumentCount - nameCount;
            }
            else
            {
                difference = 0;
            }

            var argumentNames = callInfo.ArgumentNames;

            for (var i = argumentCount - 1; i >= 0; i--)
            {
                var argument = args[i];

                var dynamicMember = argument as Member;
                if (dynamicMember == null)
                {
                    var name = argumentNames[i - difference];
                    dynamic value = argument;
                    dynamicMember = Member.Unknown(name, value);
                }

                result.Add(dynamicMember);
            }

            return result;
        }

        static IReadOnlyList<IMemberDefinition> CreateNodes(CallInfo callInfo, IReadOnlyList<object> args)
        {
            var result = new List<IMemberDefinition>();

            int difference;
            var nameCount = callInfo.ArgumentNames.Count;
            var argumentCount = args.Count;

            if (argumentCount != nameCount)
            {
                if (nameCount != args.Count(x => !(x is IMemberDefinition)))
                    throw new NotSupportedException("Unnamed arguments must be MemberDefinition instances.");

                difference = argumentCount - nameCount;
            }
            else
            {
                difference = 0;
            }

            var argumentNames = callInfo.ArgumentNames;

            for (var i = argumentCount - 1; i >= 0; i--)
            {
                var argument = args[i];

                var dynamicMember = argument as IMemberDefinition;
                if (dynamicMember == null)
                {
                    var name = argumentNames[i - difference];

                    var @delegate = argument as Delegate;
                    if (@delegate != null)
                    {
                        dynamicMember = MemberDefinition.Method(name, @delegate);
                    }
                    else
                    {
                        var type = argument as Type;

                        if (type == null)
                            throw new NotSupportedException("Definitions require a type.");

                        if (typeof(Delegate).IsAssignableFrom(type))
                        {
                            dynamicMember = MemberDefinition.EmptyMethod(name, type);
                        }
                        else
                        {
                            dynamicMember = MemberDefinition.Property(name, type);
                        }
                    }
                }

                result.Add(dynamicMember);   
            }

            return result;
        }

        public class NewTypeFactory : TypeFactory
        {
            internal NewTypeFactory() { }

            public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
            {
                var nodes = CreateValueNodes(binder.CallInfo, args);
                result = DynamicTypeBuilder.CreateTypeInstance(binder.Name, nodes);
                return true;
            }
        }

        public class DefineTypeFactory : TypeFactory
        {
            HashSet<Type> _interfaces;
            Type _parent;
            bool _serializable;
            bool _sealed;

            internal DefineTypeFactory() { }

            public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
            {
                var binderName = binder.Name.ToUpperInvariant();

                switch (binderName)
                {
                    case "WITHINTERFACE":
                        MarkInterface(args);

                        result = this;
                        break;

                    case "NOTIFYCHANGES":
                        MarkNotifiable();

                        result = this;
                        break;

                    case "INHERITFROM":
                        SetParent(args);

                        result = this;
                        break;

                    case "SEALED":
                        _sealed = true;

                        result = this;
                        break;

                    case "SERIALIZABLE":
                        _serializable = true;

                        result = this;
                        break;


                    default:
                        var nodes = CreateNodes(binder.CallInfo, args);
                        result = DynamicTypeBuilder.CreateType(binder.Name, nodes, serializable: _serializable, @sealed: _sealed, interfaces: _interfaces, parent: _parent);
                        break;
                }

                return true;
            }

            void SetParent(object[] args)
            {
                if (args.Length != 1)
                    throw new NotSupportedException("You can only have 1 parent.");

                _parent = (Type)args[0];
            }

            void MarkNotifiable()
            {
                var propertyChangedInterface = typeof(INotifyPropertyChanged);

                if (_interfaces == null)
                {
                    _interfaces = new HashSet<Type>
                                      {
                                          propertyChangedInterface
                                      };
                }
                else
                {
                    _interfaces.Add(propertyChangedInterface);
                }
            }

            void MarkInterface(object[] args)
            {
                var types = args.Cast<Type>();

                if (_interfaces == null)
                {
                    _interfaces = new HashSet<Type>(types);
                }
                else
                {
                    foreach (var type in types)
                        _interfaces.Add(type);
                }
            }
        }
    }
}
