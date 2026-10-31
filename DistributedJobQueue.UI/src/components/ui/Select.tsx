import { cn } from '../../utils/cn';

export interface SelectProps extends React.SelectHTMLAttributes<HTMLSelectElement> {
  value?: string;
  onValueChange?: (value: string) => void;
}

export const Select = ({ className, children, value, onValueChange, ...props }: SelectProps) => (
  <select
    className={cn(
      'flex h-10 w-full appearance-none rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50',
      className
    )}
    value={value}
    onChange={(e) => onValueChange?.(e.target.value)}
    {...props}
  >
    {children}
  </select>
);

export const SelectOption = ({ className, children, value, ...props }: React.OptionHTMLAttributes<HTMLOptionElement> & { value: string }) => (
  <option className={cn('relative', className)} value={value} {...props}>
    {children}
  </option>
);