import { Button } from "@/components/ui/button";
import { loginUrl } from "@/lib/api";

/** Shown by pages that need a session; sign-in returns the user to the current page. */
export function SignInPrompt({ message }: { message: string }) {
  return (
    <div className="mx-auto mt-12 max-w-md text-center">
      <p className="mb-4 text-slate-600">{message}</p>
      <a href={loginUrl()}>
        <Button>Sign in</Button>
      </a>
    </div>
  );
}
